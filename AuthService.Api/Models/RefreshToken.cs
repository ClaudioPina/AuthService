namespace AuthService.Api.Models
{
    /// <summary>
    /// Entidad que representa un refresh token persistido.
    /// Se usa en algunos flujos/reportes internos del dominio de autenticación.
    /// </summary>
    public class RefreshToken
    {
        /// <summary>
        /// Clave primaria del registro.
        /// </summary>
        public long IdRefresh { get; set; }

        /// <summary>
        /// ID del usuario dueño del token.
        /// </summary>
        public long IdUsuario { get; set; }

        /// <summary>
        /// Valor del token (según el flujo, puede estar en texto plano o hasheado).
        /// </summary>
        public string Token { get; set; } = null!;

        /// <summary>
        /// Fecha/hora de expiración del token.
        /// </summary>
        public DateTime ExpiraEn { get; set; }

        /// <summary>
        /// Estado lógico del token (1 activo, 0 inactivo).
        /// </summary>
        public int Estado { get; set; }
    }
}
