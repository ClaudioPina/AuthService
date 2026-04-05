namespace AuthService.Api.Dtos.Auth
{
    /// <summary>
    /// Payload de entrada para <c>POST /auth/reset-password</c>.
    /// </summary>
    public class ResetPasswordRequest
    {
        /// <summary>
        /// Token de recuperación recibido por email.
        /// </summary>
        public string Token { get; set; } = null!;

        /// <summary>
        /// Nueva contraseña en texto plano.
        /// </summary>
        public string NewPassword { get; set; } = null!;

        /// <summary>
        /// Confirmación de la nueva contraseña. Debe coincidir con NewPassword.
        /// </summary>
        public string NewPasswordConfirmacion { get; set; } = null!;
    }
}
