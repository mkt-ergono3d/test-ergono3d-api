using System;
using System.Diagnostics;

public static class PostgresBackup
{
    /// <summary>
    /// 备份整个 PostgreSQL 数据库到文件
    /// </summary>
    /// <param name="host">数据库主机</param>
    /// <param name="port">端口</param>
    /// <param name="database">数据库名</param>
    /// <param name="username">用户名</param>
    /// <param name="password">密码</param>
    /// <param name="outputFile">输出文件路径（带文件名）</param>
    /// <param name="pgDumpPath">pg_dump 可执行文件路径</param>
    public static void BackupDatabase()
    {
        // 设置环境变量 PGPASSWORD 避免交互输入
        var psi = new ProcessStartInfo
        {
            FileName = $"{DateTime.Now:yyyy-MM-dd HH-mm-ss}.sql",
            Arguments = $"-h 3.25.180.105 -p 5432 -U postgres -F c -b -v -f \"C://backup/sql\" insole",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // 设置密码环境变量
        psi.EnvironmentVariables["PGPASSWORD"] = Environment.GetEnvironmentVariable("PGPASSWORD") ?? throw new InvalidOperationException("Set PGPASSWORD before running a database backup.");

        using (var process = new Process())
        {
            process.StartInfo = psi;
            process.OutputDataReceived += (sender, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
            process.ErrorDataReceived += (sender, e) => { if (e.Data != null) Console.WriteLine("ERR: " + e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
        }
    }
}