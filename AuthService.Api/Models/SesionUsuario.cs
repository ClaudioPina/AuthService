namespace AuthService.Api.Models
{
    /// <summary>
    /// Representa una sesión activa de usuario en la base de datos.
    /// Cada sesión tiene un refresh token (guardado como hash SHA-256) y datos de auditoría.
    /// </summary>
    public class SesionUsuario
    {
        public long IdSesion { get; set; }
        public long IdUsuario { get; set; }

        /// <summary>Hash SHA-256 del refresh token. Nunca se almacena el token plano.</summary>
        public string TokenHash { get; set; } = null!;

        public DateTime ExpiraEn { get; set; }
        public int Estado { get; set; }

        public string? UserAgent { get; set; }
        public string? IpOrigen { get; set; }
        public DateTime Creacion { get; set; }
    }
}
