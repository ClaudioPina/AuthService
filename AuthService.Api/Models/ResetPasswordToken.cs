namespace AuthService.Api.Models
{
    /// <summary>
    /// Entidad que representa un token de recuperación de contraseña.
    /// Corresponde a la tabla <c>RESET_PASSWORD</c>.
    /// </summary>
    public class ResetPasswordToken
    {
        /// <summary>
        /// Clave primaria del token de reset.
        /// </summary>
        public long IdReset { get; set; }

        /// <summary>
        /// Usuario al que pertenece el token.
        /// </summary>
        public long IdUsuario { get; set; }

        /// <summary>
        /// Valor del token almacenado en BD (en este proyecto se persiste hasheado).
        /// </summary>
        public string Token { get; set; } = "";

        /// <summary>
        /// Fecha/hora de expiración.
        /// </summary>
        public DateTime ExpiraEn { get; set; }

        /// <summary>
        /// Estado lógico (1 disponible, 0 usado/inactivo).
        /// </summary>
        public int Estado { get; set; }

        /// <summary>
        /// Tipo de token: "reset" (forgot-password) o "change_confirm" (cambio confirmado por email).
        /// </summary>
        public string Tipo { get; set; } = "reset";

        /// <summary>
        /// Hash BCrypt de la nueva contraseña. Solo se usa cuando Tipo = "change_confirm".
        /// </summary>
        public string? NuevoPasswordHash { get; set; }
    }
}
