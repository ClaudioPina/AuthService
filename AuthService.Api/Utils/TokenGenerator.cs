using System.Security.Cryptography;
using System.Text;

namespace AuthService.Api.Utils
{
    public static class TokenGenerator
    {
        // Genera un token aleatorio en HEX (ej. 64 caracteres)
        public static string GenerateToken(int bytesLength = 32)
        {
            var bytes = new byte[bytesLength];
            RandomNumberGenerator.Fill(bytes);
            var sb = new StringBuilder(bytesLength * 2);

            foreach (var b in bytes)
            {
                sb.Append(b.ToString("x2")); // convierte a hex
            }

            return sb.ToString();
        }

        public static string HashToken(string token)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(token);
            var hashBytes = sha.ComputeHash(bytes);
            return Convert.ToHexString(hashBytes).ToLower();
        }
    }
}
