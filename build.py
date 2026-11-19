from datetime import datetime
import argparse
import asyncio
import shutil
import platform
import sys
import os
import re


def invoke(command, expected_return=0):
    code = os.system(command)
    if code != expected_return:
        sys.exit(-1)


def auto_version(image):
    prefix = datetime.now().strftime("%Y%m%d")
    pattern = re.compile(prefix + "-(?P<index>\d+)")
    index = -1

    p = os.popen(f"docker images")
    lines = p.read().strip().split("\n")[1:]
    for line in lines:
        line = line.split()
        if line and line[0] == image:
            g = re.match(pattern, line[1])
            if g:
                index = max(index, int(g["index"]))

    return f"{prefix}-{index + 1}"


def build_projects(args):
    # 1. 清除已构建的项目
    publish_dir = "Web/bin/Release/net8.0/publish"
    if os.path.exists(publish_dir):
        shutil.rmtree(publish_dir)
    
    wwwroot = "Web/wwwroot"
    if os.path.exists(wwwroot):
        shutil.rmtree(wwwroot)

    # 2. 构建前端项目
    os.chdir("../web")
    if args.prod:
        invoke("npm run build-prod")
    else:
        invoke("npm run build-only")

    shutil.copytree("public/build", "../InsoleApi/Web/wwwroot")
    shutil.copytree("src/assets", "../InsoleApi/Web/wwwroot/src/assets")
    # shutil.copytree("src/static", "../Web/Web/wwwroot/src/static")
    shutil.copytree("../InsoleApi/Web/static", "../InsoleApi/Web/wwwroot/src/static")

    # 3. 构建后端项目
    os.chdir("../InsoleApi")
    invoke("dotnet publish")


async def deploy_int(image, version):
    import asyncssh

    txt = open("deploy/int.yaml", encoding="utf8").read()
    txt = txt.replace("{{image}}", image).replace("{{version}}", version)

    async with asyncssh.connect(host=os.environ["INSOLE_DEPLOY_INT_HOST"], username=os.environ["INSOLE_DEPLOY_INT_USER"], password=os.environ["INSOLE_DEPLOY_INT_PASSWORD"]) as conn:
        async with conn.start_sftp_client() as sftp:
            async with sftp.open('/root/web-int/docker-compose.yml', 'w') as f:
                await f.write(txt)

        await conn.run('docker login -u publish-1729521749073 -p ca063169dd40e6395787201f541e7d3749453a12 g-ykui3682-docker.pkg.coding.net')
        if platform.system() == "Windows":
            await conn.run("cd /root/web-int && docker-compose up -d")
        else:
            await conn.run("cd /root/web-int && docker-compose up -d", stdout=sys.stdout, stderr=sys.stderr)


async def deploy_prod(image, version):
    import asyncssh

    txt = open("deploy/prod.yaml", encoding="utf8").read()
    txt = txt.replace("{{image}}", image).replace("{{version}}", version)

    async with asyncssh.connect(host=os.environ["INSOLE_DEPLOY_PROD_HOST"], username=os.environ["INSOLE_DEPLOY_PROD_USER"], password=os.environ["INSOLE_DEPLOY_PROD_PASSWORD"]) as conn:
        async with conn.start_sftp_client() as sftp:
            async with sftp.open('/root/web-prod/docker-compose.yml', 'w') as f:
                await f.write(txt)

        await conn.run('docker login -u publish-1729521749073 -p ca063169dd40e6395787201f541e7d3749453a12 g-ykui3682-docker.pkg.coding.net')
        if platform.system() == "Windows":
            await conn.run("cd /root/web-prod && docker-compose up -d")
        else:
            await conn.run("cd /root/web-prod && docker-compose up -d", stdout=sys.stdout, stderr=sys.stderr)


async def main(args):
    version = args.version
    image = "g-ykui3682-docker.pkg.coding.net/insole/publish/web"
    
    if version == "auto":
        version = auto_version(image)

    code = os.system(f"docker inspect {image}:{version}")
    if code != 0:
        build_projects(args)

        invoke(f"docker login -u publish-1729521749073 -p ca063169dd40e6395787201f541e7d3749453a12 g-ykui3682-docker.pkg.coding.net")
        invoke(f"docker build --platform linux/amd64 -t {image}:{version} .")
        invoke(f"docker push {image}:{version}")
    
    if args.int:
        await deploy_int(image, version)

    if args.prod:
        await deploy_prod(image, version)

    print(f"version: {version}")
    print(datetime.now())
    

if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", type=str, default="auto")
    parser.add_argument("--dev", action='store_true')
    parser.add_argument("--int", action='store_true')
    parser.add_argument("--prod", action='store_true')

    if platform.system() == "Windows":
        import win32api
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

    loop = asyncio.get_event_loop()
    loop.run_until_complete(main(parser.parse_args()))