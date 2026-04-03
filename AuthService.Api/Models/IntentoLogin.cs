namespace AuthService.Api.Models
{
    /// <summary>
    /// Entidad para mapear la tabla <c>INTENTOS_LOGIN</c>.
    /// Permite aplicar bloqueo temporal tras múltiples intentos fallidos.
    /// </summary>
    public class IntentoLogin
    {
        /// <summary>
        /// Clave primaria del intento.
        /// </summary>
        public long IdIntento { get; set; }

        /// <summary>
        /// Email sobre el que se están contabilizando intentos.
        /// </summary>
        public string Email { get; set; } = null!;

        /// <summary>
        /// IP de origen del último intento.
        /// </summary>
        public string IpOrigen { get; set; } = null!;

        /// <summary>
        /// Contador acumulado de intentos fallidos para el email.
        /// </summary>
        public int Intentos { get; set; }

        /// <summary>
        /// Timestamp del intento más reciente.
        /// </summary>
        public DateTime UltimoIntento { get; set; }

        /// <summary>
        /// Si tiene valor y es futura, la cuenta está temporalmente bloqueada.
        /// </summary>
        public DateTime? BloqueadoHasta { get; set; }
    }
}
