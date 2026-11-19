using System.Security.Cryptography;
using System.Text;

namespace InsoleApi.Utils
{
    public static class EncryptionHelper
    {
        public static string Sha1(string content)
        {
            var sha1 = SHA1.Create();
            var hashByte = sha1.ComputeHash(Encoding.UTF8.GetBytes(content));
            sha1.Dispose();

            var sb = new StringBuilder();
            foreach (var b in hashByte)
            {
                sb.Append(b.ToString("X2"));
            }
            return sb.ToString();
        }
    }
}
