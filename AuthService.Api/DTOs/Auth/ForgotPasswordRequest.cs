namespace AuthService.Api.Dtos.Auth
{
    /// <summary>
    /// Payload de entrada para <c>POST /auth/forgot-password</c>.
    /// </summary>
    public class ForgotPasswordRequest
    {
        /// <summary>
        /// Email del usuario que solicita recuperar su contraseña.
        /// </summary>
        public string Email { get; set; } = string.Empty;
    }
}
