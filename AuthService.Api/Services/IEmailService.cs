namespace AuthService.Api.Services
{
    /// <summary>
    /// Contrato para el servicio de emails. Usar una interfaz permite
    /// reemplazar la implementación real por un fake en los tests.
    /// </summary>
    public interface IEmailService
    {
        Task SendVerificationEmailAsync(string toEmail, string verificationLink);
        Task SendPasswordResetEmailAsync(string toEmail, string resetLink);
        Task SendPasswordChangeVerificationEmailAsync(string toEmail, string confirmationLink);
        Task SendNewLoginNotificationAsync(string toEmail, string ip, string userAgent);
        Task SendPasswordChangedNotificationAsync(string toEmail);
    }
}
