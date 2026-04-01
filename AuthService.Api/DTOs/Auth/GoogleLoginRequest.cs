namespace AuthService.Api.Dtos.Auth
{
    /// <summary>
    /// Request para login con Google.
    /// El frontend obtiene el IdToken desde la librería de Google Sign-In
    /// y lo envía directamente al backend para validación.
    /// </summary>
    public class GoogleLoginRequest
    {
        /// <summary>
        /// Token de identidad emitido por Google tras la autenticación del usuario.
        /// Se valida criptográficamente usando las claves públicas de Google.
        /// </summary>
        public string IdToken { get; set; } = null!;
    }
}
