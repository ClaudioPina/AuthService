namespace AuthService.Api.Dtos.Auth
{
    /// <summary>
    /// Payload de entrada para el endpoint <c>POST /auth/register</c>.
    /// Incluye los datos mínimos para crear una cuenta local (email + password).
    /// </summary>
    public class RegisterRequest
    {
        /// <summary>
        /// Email del usuario. Se normaliza en el servicio (trim + lowercase)
        /// antes de consultar/guardar en base de datos.
        /// </summary>
        public string Email { get; set; } = string.Empty;

        /// <summary>
        /// Nombre visible del usuario. Es opcional y puede venir vacío.
        /// </summary>
        public string? Nombre { get; set; }

        /// <summary>
        /// Contraseña en texto plano enviada por el cliente.
        /// Se valida con <c>PasswordPolicy</c> y luego se hashea con BCrypt.
        /// </summary>
        public string Password { get; set; } = string.Empty;

        /// <summary>
        /// Confirmación de la contraseña. Debe coincidir con <see cref="Password"/>.
        /// </summary>
        public string PasswordConfirmacion { get; set; } = string.Empty;
    }
}
