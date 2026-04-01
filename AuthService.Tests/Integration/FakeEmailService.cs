using System.Collections.Concurrent;
using AuthService.Api.Services;

namespace AuthService.Tests.Integration
{
    /// <summary>
    /// Implementación falsa de IEmailService para tests de integración.
    /// Registra los emails "enviados" en una lista para poder verificarlos.
    /// No hace llamadas HTTP reales a Resend — así los tests no requieren
    /// una API key real ni conectividad externa.
    /// </summary>
    public class FakeEmailService : IEmailService
    {
        public ConcurrentBag<(string To, string Link)> VerificationEmails { get; } = new();
        public ConcurrentBag<(string To, string Link)> ResetEmails { get; } = new();

        public Task SendVerificationEmailAsync(string toEmail, string verificationLink)
        {
            VerificationEmails.Add((toEmail, verificationLink));
            return Task.CompletedTask;
        }

        public Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
        {
            ResetEmails.Add((toEmail, resetLink));
            return Task.CompletedTask;
        }

        public ConcurrentBag<(string To, string Ip, string UserAgent)> LoginNotifications { get; } = new();
        public ConcurrentBag<string> PasswordChangedNotifications { get; } = new();

        public Task SendNewLoginNotificationAsync(string toEmail, string ip, string userAgent)
        {
            LoginNotifications.Add((toEmail, ip, userAgent));
            return Task.CompletedTask;
        }

        public Task SendPasswordChangedNotificationAsync(string toEmail)
        {
            PasswordChangedNotifications.Add(toEmail);
            return Task.CompletedTask;
        }
    }
}
