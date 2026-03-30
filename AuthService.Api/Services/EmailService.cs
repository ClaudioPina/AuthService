using Resend;

namespace AuthService.Api.Services
{
    /// <summary>
    /// Implementación real del servicio de email usando Resend SDK v0.2.2.
    /// Configuración requerida en appsettings.json:
    ///   Email:ResendApiKey, Email:FromAddress, Email:FromName
    /// </summary>
    public class EmailService : IEmailService
    {
        private readonly IResend _resend;
        private readonly IConfiguration _config;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IResend resend, IConfiguration config, ILogger<EmailService> logger)
        {
            _resend = resend;
            _config = config;
            _logger = logger;
        }

        /// <summary>
        /// Envía el correo de verificación de cuenta.
        /// El link contiene un token único que expira en 24 horas.
        /// Mientras no tengas dominio propio en Resend, usa FromAddress = "onboarding@resend.dev".
        /// </summary>
        public async Task SendVerificationEmailAsync(string toEmail, string verificationLink)
        {
            var from = $"{_config["Email:FromName"]} <{_config["Email:FromAddress"]}>";

            var message = new EmailMessage
            {
                From     = from,
                Subject  = "Verifica tu cuenta",
                HtmlBody = $"""
                    <h2>Bienvenido a AuthService</h2>
                    <p>Haz clic en el siguiente enlace para verificar tu email:</p>
                    <a href="{verificationLink}">Verificar email</a>
                    <p>Este enlace expira en 24 horas.</p>
                    <p>Si no creaste esta cuenta, ignora este correo.</p>
                    """
            };
            message.To.Add(toEmail);

            await _resend.EmailSendAsync(message);
            _logger.LogInformation("Email de verificación enviado a {Email}", toEmail);
        }

        /// <summary>
        /// Envía el correo de recuperación de contraseña.
        /// El link contiene un token único que expira en 1 hora.
        /// </summary>
        public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
        {
            var from = $"{_config["Email:FromName"]} <{_config["Email:FromAddress"]}>";

            var message = new EmailMessage
            {
                From     = from,
                Subject  = "Recuperación de contraseña",
                HtmlBody = $"""
                    <h2>Recuperación de contraseña</h2>
                    <p>Haz clic en el siguiente enlace para restablecer tu contraseña:</p>
                    <a href="{resetLink}">Restablecer contraseña</a>
                    <p>Este enlace expira en 1 hora.</p>
                    <p>Si no solicitaste este cambio, ignora este correo.</p>
                    """
            };
            message.To.Add(toEmail);

            await _resend.EmailSendAsync(message);
            _logger.LogInformation("Email de reset de contraseña enviado a {Email}", toEmail);
        }
    }
}
