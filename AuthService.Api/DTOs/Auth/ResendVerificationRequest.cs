namespace AuthService.Api.Dtos.Auth
{
    /// <summary>
    /// Payload de entrada para <c>POST /auth/resend-verification</c>.
    /// </summary>
    public class ResendVerificationRequest
    {
        /// <summary>
        /// Email del usuario que necesita un nuevo link de verificación.
        /// </summary>
        public string Email { get; set; } = string.Empty;
    }
}
