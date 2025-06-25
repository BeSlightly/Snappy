using System.Security.Cryptography;
using System.Text;

namespace Snappy.Utils
{
    public static class Crypto
    {
        public static string GetHash(byte[] bytes)
        {
            using var sha1 = SHA1.Create();
            return ToHashedString(sha1.ComputeHash(bytes));
        }

        private static string ToHashedString(byte[] bytes)
        {
            StringBuilder sb = new();
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("X2"));
            }
            return sb.ToString();
        }
    }
}
