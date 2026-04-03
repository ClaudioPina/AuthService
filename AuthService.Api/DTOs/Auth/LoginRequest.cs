namespace AuthService.Api.Dtos.Auth
{
    /// <summary>
    /// Payload de entrada para <c>POST /auth/login</c>.
    /// </summary>
    public class LoginRequest
    {
        /// <summary>
        /// Email de la cuenta a autenticar.
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Contraseña en texto plano a verificar contra el hash almacenado.
        /// </summary>
        public string Password { get; set; } = string.Empty;
    }
}
