import { spawn } from 'node:child_process'
import { createReadStream } from 'node:fs'
import { access, mkdir, readFile, rename, stat, unlink, writeFile } from 'node:fs/promises'
import http from 'node:http'
import path from 'node:path'
import { setTimeout as sleep } from 'node:timers/promises'

const root = path.resolve(process.argv[2])
const settings = JSON.parse(await readFile(path.join(root, 'private', 'runtime.json'), 'utf8'))
const stateFile = path.join(root, 'state', 'active.json')
const inbox = path.join(root, 'pending')
const types = ['api', 'landing', 'app']
const ports = { api: [15033, 15034], landing: [15176, 15177] }
const host = '127.0.0.1'
const state = await readJson(stateFile) ?? {}
const children = new Map()
let stopping = false
let postgres

for (const type of types) state[type] ??= {}
await mkdir(inbox, { recursive: true })

async function readJson(file) {
  try { return JSON.parse(await readFile(file, 'utf8')) }
  catch (error) { if (error.code !== 'ENOENT') throw error }
}

async function save(file, value) {
  const temporary = `${file}.${process.pid}.tmp`
  await writeFile(temporary, JSON.stringify(value), 'utf8')
  await rename(temporary, file)
}

function releasePath(type, commit) {
  if (!/^[0-9a-f]{40}$/.test(commit ?? '')) throw new Error('Invalid release commit')
  return path.join(root, 'releases', type, commit)
}

function start(file, args, cwd, extraEnv = {}) {
  const child = spawn(file, args, { cwd, env: { ...process.env, ...extraEnv }, windowsHide: true, stdio: 'ignore' })
  child.once('error', () => {})
  return child
}

function alive(child) {
  return Boolean(child?.pid && child.exitCode === null && child.signalCode === null && !child.killed)
}

async function run(file, args, cwd, extraEnv) {
  const child = start(file, args, cwd, extraEnv)
  return new Promise(resolve => {
    child.once('error', () => resolve(false))
    child.once('exit', code => resolve(code === 0))
  })
}

async function ensurePostgres() {
  if (alive(postgres)) return
  const data = path.join(root, 'database')
  const bin = settings.postgresBin
  if (!(await stat(path.join(data, 'PG_VERSION')).catch(() => null))) {
    const passwordFile = path.join(root, 'private', 'database-bootstrap')
    const initialized = await run(path.join(bin, 'initdb.exe'), [
      '-D', data, '--username=ergono_test', '--auth=scram-sha-256', '--encoding=UTF8', '--locale=C', `--pwfile=${passwordFile}`
    ], root)
    if (!initialized) throw new Error('Test PostgreSQL initialization failed')
  }
  postgres = start(path.join(bin, 'postgres.exe'), [
    '-D', data, '-h', host, '-p', '55432', '-c', 'logging_collector=on', '-c', 'log_rotation_size=10240',
    '-c', 'log_filename=postgresql-%a.log', '-c', 'log_truncate_on_rotation=on'
  ], root)
}

async function ensureSchema(dir, environment) {
  const marker = path.join(root, 'database', 'schema-initialized')
  if (await stat(marker).catch(() => null)) return
  const pgIsReady = path.join(settings.postgresBin, 'pg_isready.exe')
  for (let attempt = 0; attempt < 30; attempt += 1) {
    if (!alive(postgres)) break
    if (await run(pgIsReady, ['-h', host, '-p', '55432', '-q'], root)) {
      const created = await run(path.join(dir, 'InsoleApi.exe'), ['--initialize-test-database'], dir, environment)
      if (!created) break
      await writeFile(marker, '1', 'utf8')
      return
    }
    await sleep(500)
  }
  throw new Error('Test database schema initialization failed')
}

async function probe(port, route, timeout = 2500) {
  return fetch(`http://${host}:${port}${route}`, { redirect: 'error', signal: AbortSignal.timeout(timeout) })
}

async function openRelease(type, commit, port) {
  const dir = releasePath(type, commit)
  if (type === 'app') {
    const html = await readFile(path.join(dir, 'index.html'), 'utf8')
    const entries = [...html.matchAll(/(?:src|href)="\/app\/([^"?]+\.(?:js|css))"/g)]
    if (!entries.some(match => match[1].endsWith('.js'))) throw new Error('Missing Vite entry')
    for (const [, file] of entries) await access(path.join(dir, file))
    return { dir }
  }

  const processEnv = type === 'api' ? {
    ...settings.api,
    DOTNET_ENVIRONMENT: 'Test',
    ASPNETCORE_ENVIRONMENT: 'Test',
    ASPNETCORE_URLS: `http://${host}:${port}`,
    Deployment__Release: commit,
    Deployment__LogDirectory: path.join(root, 'logs', 'api')
  } : {
    PORT: String(port), HOST: host, HEEL_INSET_ENABLED: '1', PUBLIC_SITE_ROOT: path.join(dir, 'public')
  }
  if (type === 'api') await ensureSchema(dir, processEnv)
  const child = type === 'api'
    ? start(path.join(dir, 'InsoleApi.exe'), [], dir, processEnv)
    : start(process.execPath, ['web-server.js'], dir, processEnv)
  try {
    const deadline = Date.now() + 45000
    while (alive(child) && Date.now() < deadline) {
      try {
        if (type === 'api') {
          const response = await probe(port, '/health/ready')
          if (response.ok && (await response.json()).release === commit) return { child, port }
        } else {
          const home = await probe(port, '/')
          await home.body?.cancel()
          const heel = await probe(port, '/api/tools/heel-inset/status')
          if (home.ok && heel.ok && (await heel.json()).enabled === true) return { child, port }
        }
      } catch { /* Wait for startup before rejecting the candidate. */ }
      await sleep(400)
    }
    throw new Error('Candidate readiness failed')
  } catch (error) {
    if (alive(child)) child.kill()
    throw error
  }
}

async function activate(type, commit, failedCommit) {
  const old = children.get(type)
  const port = ports[type]?.find(candidate => candidate !== old?.port)
  const candidate = await openRelease(type, commit, port)
  const before = state[type]
  state[type] = commit === before.current
    ? { ...before, attempts: 0 }
    : { current: commit, previous: failedCommit ? undefined : before.current, lastRejected: failedCommit ?? before.lastRejected, attempts: 0 }
  try {
    await save(stateFile, state)
  } catch (error) {
    state[type] = before
    if (alive(candidate.child)) candidate.child.kill()
    throw error
  }
  children.set(type, { ...candidate, misses: 0 })
  if (alive(old?.child)) old.child.kill()
}

async function usePending(type) {
  const file = path.join(inbox, `${type}.json`)
  const pending = await readJson(file)
  if (!pending) return
  // Leave a newer pending file intact when a build arrives during the probe.
  const claimed = `${file}.processing`
  await rename(file, claimed)
  const { commit } = await readJson(claimed)
  try {
    if (state[type].current !== commit || !children.has(type)) await activate(type, commit)
  } catch (error) {
    console.error(`${type} candidate ${commit ?? '(missing)'} rejected: ${error.code ?? error.message}`)
    state[type].lastRejected = commit
    await save(stateFile, state)
  } finally {
    await unlink(claimed)
  }
}

async function recover(type) {
  const current = state[type]
  if (!current.current) return
  const running = children.get(type)
  if (type === 'app' && running) return
  if (alive(running?.child)) {
    try {
      const route = type === 'api' ? '/health' : '/api/tools/heel-inset/status'
      const result = await probe(running.port, route)
      await result.body?.cancel()
      running.misses = result.ok ? 0 : running.misses + 1
    } catch { running.misses += 1 }
    if (running.misses < 3) return
  }
  try {
    await activate(type, current.current)
  } catch {
    current.attempts = (current.attempts ?? 0) + 1
    if (current.previous && current.attempts >= 2) {
      try {
        await activate(type, current.previous, current.current)
        return
      } catch { /* Keep both commits for the next recovery attempt. */ }
    }
    await save(stateFile, state)
  }
}

const mime = {
  '.html': 'text/html; charset=utf-8', '.js': 'text/javascript; charset=utf-8', '.mjs': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8', '.json': 'application/json', '.png': 'image/png', '.webp': 'image/webp',
  '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.svg': 'image/svg+xml', '.ico': 'image/x-icon', '.gif': 'image/gif',
  '.woff': 'font/woff', '.woff2': 'font/woff2', '.ttf': 'font/ttf', '.wasm': 'application/wasm', '.xml': 'application/xml',
  '.txt': 'text/plain; charset=utf-8', '.pdf': 'application/pdf', '.avif': 'image/avif'
}

function reply(response, status) { response.writeHead(status).end() }

async function appFile(request, response, pathname) {
  if (!['GET', 'HEAD'].includes(request.method)) return reply(response, 405)
  if (pathname === '/app') return response.writeHead(308, { Location: '/app/' }).end()
  const active = children.get('app')
  if (!active) return reply(response, 503)
  const relative = decodeURIComponent(pathname.slice('/app/'.length))
  if (relative.includes('\\') || relative.includes(':') || relative.includes('\0') || relative.split('/').some(part => part.startsWith('.'))) {
    return reply(response, 404)
  }
  const extension = path.extname(relative).toLowerCase()
  const file = path.resolve(active.dir, extension ? relative : 'index.html')
  if (!file.startsWith(active.dir + path.sep) || extension === '.map') return reply(response, 404)
  const info = await stat(file).catch(() => null)
  if (!info?.isFile()) return reply(response, 404)
  response.writeHead(200, { 'Content-Type': mime[path.extname(file).toLowerCase()] ?? 'application/octet-stream', 'Content-Length': info.size })
  if (request.method === 'HEAD') return response.end()
  const stream = createReadStream(file)
  stream.on('error', () => response.destroy())
  response.on('close', () => stream.destroy())
  stream.pipe(response)
}

function forward(request, response, type) {
  const active = children.get(type)
  if (!alive(active?.child)) return reply(response, 503)
  const upstream = http.request({ hostname: host, port: active.port, method: request.method, path: request.url, headers: request.headers }, incoming => {
    response.writeHead(incoming.statusCode, incoming.headers)
    incoming.pipe(response)
  })
  upstream.on('error', () => response.headersSent ? response.destroy() : reply(response, 502))
  response.on('close', () => upstream.destroy())
  request.pipe(upstream)
}

const gateway = http.createServer((request, response) => {
  response.setHeader('X-Robots-Tag', 'noindex, nofollow')
  const pathname = request.url.split('?', 1)[0]
  if (!pathname.startsWith('/') || pathname.startsWith('//')) return reply(response, 400)
  if (pathname === '/app' || pathname.startsWith('/app/')) return void appFile(request, response, pathname).catch(() => reply(response, 400))
  const heel = pathname.startsWith('/api/tools/heel-inset/')
  const api = pathname === '/api' || pathname.startsWith('/api/') || pathname === '/health' || pathname.startsWith('/health/')
  forward(request, response, api && !heel ? 'api' : 'landing')
})

gateway.listen(settings.webPort, host)
for (const signal of ['SIGINT', 'SIGTERM']) process.once(signal, () => {
  stopping = true
  gateway.close()
  for (const { child } of children.values()) if (alive(child)) child.kill()
  if (alive(postgres)) postgres.kill()
})

// A completed build may have been claimed just before Windows stopped the task.
for (const type of types) {
  const file = path.join(inbox, `${type}.json`)
  const claimed = `${file}.processing`
  if (await readJson(claimed)) {
    if (await readJson(file)) await unlink(claimed)
    else await rename(claimed, file)
  }
}

while (!stopping) {
  try { await ensurePostgres() }
  catch (error) { console.error(`PostgreSQL supervisor: ${error.code ?? error.message}`) }
  for (const type of types) {
    try { await usePending(type); await recover(type) }
    catch (error) { console.error(`${type} supervisor: ${error.code ?? error.name}`) }
  }
  await sleep(5000)
}
