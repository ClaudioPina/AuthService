namespace AuthService.Api.Dtos.Auth
{
    /// <summary>
    /// Payload de entrada para <c>POST /auth/change-password</c>.
    /// Requiere JWT válido porque opera sobre el usuario autenticado.
    /// </summary>
    public class ChangePasswordRequest
    {
        /// <summary>
        /// Contraseña actual del usuario, usada para confirmar identidad.
        /// </summary>
        public string PasswordActual { get; set; } = null!;

        /// <summary>
        /// Nueva contraseña que reemplazará a la actual tras las validaciones.
        /// </summary>
        public string PasswordNueva { get; set; } = null!;

        /// <summary>
        /// Confirmación de la nueva contraseña. Debe coincidir con <see cref="PasswordNueva"/>.
        /// </summary>
        public string PasswordNuevaConfirmacion { get; set; } = null!;
    }
}
