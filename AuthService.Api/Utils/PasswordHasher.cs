using BCrypt.Net;

namespace AuthService.Api.Utils
{
    /// <summary>
    /// Wrapper centralizado para hashing y verificación de contraseñas con BCrypt.
    /// Mantenerlo en una sola clase evita duplicar configuración de seguridad
    /// en distintos puntos del código.
    /// </summary>
    public static class PasswordHasher
    {
        /// <summary>
        /// Genera un hash BCrypt a partir de una contraseña en texto plano.
        /// Se usa <c>workFactor: 12</c> como balance entre seguridad y rendimiento.
        /// </summary>
        public static string HashPassword(string plainPassword)
        {
            return BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 12);
        }

        /// <summary>
        /// Verifica si una contraseña en texto plano coincide con un hash BCrypt.
        /// </summary>
        public static bool VerifyPassword(string plainPassword, string hash)
        {
            return BCrypt.Net.BCrypt.Verify(plainPassword, hash);
        }
    }
}
