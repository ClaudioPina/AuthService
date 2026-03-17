using BCrypt.Net;

namespace AuthService.Api.Utils
{
    public static class PasswordHasher
    {
        public static string HashPassword(string plainPassword)
        {
            return BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 12);
        }

        public static bool VerifyPassword(string plainPassword, string hash)
        {
            return BCrypt.Net.BCrypt.Verify(plainPassword, hash);
        }
    }
}
