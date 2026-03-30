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
    }
}
