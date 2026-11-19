using System;
using System.IO;
using System.Threading.Tasks;
namespace InsoleApi.Utils
{
    public static class STLManager
    {
        /// <summary>
        /// 保存 Base64 字符串到 wwwroot 下的文件
        /// </summary>
        public static async Task SaveAsync(string base64, string fileName)
        {
            // 去掉 data:xxx;base64, 前缀
            var index = base64.IndexOf(",");
            if (index >= 0)
                base64 = base64.Substring(index + 1);

            byte[] bytes = Convert.FromBase64String(base64);

            string stlsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "stls");

            // 保证目录存在
            if (!Directory.Exists(stlsDir))
                Directory.CreateDirectory(stlsDir);

            string filePath = Path.Combine(stlsDir, fileName);

            await File.WriteAllBytesAsync(filePath, bytes);
#if !DEBUG
            await File.WriteAllBytesAsync($"C://backup/stls/{fileName}", bytes);
#endif
        }

        /// <summary>
        /// 按文件名从 wwwroot 读取文件并返回 Base64 字符串
        /// </summary>
        public static async Task<string?> ReadAsync(string fileName)
        {
            string stlsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "stls");
            if (!Directory.Exists(stlsDir))
                Directory.CreateDirectory(stlsDir);
            string filePath = Path.Combine(stlsDir, fileName);

            if (!File.Exists(filePath)) return null;

            byte[] bytes = await File.ReadAllBytesAsync(filePath);
            return Convert.ToBase64String(bytes);
        }
    }
}
