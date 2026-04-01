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

        /// <summary>
        /// Notifica al usuario sobre un nuevo inicio de sesión en su cuenta.
        /// Se envía en cada login exitoso para que el usuario detecte accesos no autorizados.
        /// </summary>
        public async Task SendNewLoginNotificationAsync(string toEmail, string ip, string userAgent)
        {
            var from = $"{_config["Email:FromName"]} <{_config["Email:FromAddress"]}>";

            var message = new EmailMessage
            {
                From     = from,
                Subject  = "Nuevo inicio de sesión en tu cuenta",
                HtmlBody = $"""
                    <h2>Nuevo inicio de sesión detectado</h2>
                    <p>Se ha iniciado sesión en tu cuenta con los siguientes datos:</p>
                    <ul>
                        <li><strong>IP:</strong> {ip}</li>
                        <li><strong>Dispositivo:</strong> {userAgent}</li>
                        <li><strong>Fecha:</strong> {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC</li>
                    </ul>
                    <p>Si fuiste tú, puedes ignorar este correo.</p>
                    <p>Si no fuiste tú, cambia tu contraseña inmediatamente.</p>
                    """
            };
            message.To.Add(toEmail);

            await _resend.EmailSendAsync(message);
            _logger.LogInformation("Notificación de login enviada a {Email}", toEmail);
        }

        /// <summary>
        /// Notifica al usuario que su contraseña fue cambiada.
        /// Útil para detectar cambios no autorizados.
        /// </summary>
        public async Task SendPasswordChangedNotificationAsync(string toEmail)
        {
            var from = $"{_config["Email:FromName"]} <{_config["Email:FromAddress"]}>";

            var message = new EmailMessage
            {
                From     = from,
                Subject  = "Tu contraseña ha sido cambiada",
                HtmlBody = $"""
                    <h2>Contraseña actualizada</h2>
                    <p>Tu contraseña fue cambiada el {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC.</p>
                    <p>Todas tus sesiones activas han sido cerradas por seguridad.</p>
                    <p>Si no realizaste este cambio, contacta al soporte inmediatamente.</p>
                    """
            };
            message.To.Add(toEmail);

            await _resend.EmailSendAsync(message);
            _logger.LogInformation("Notificación de cambio de contraseña enviada a {Email}", toEmail);
        }
    }
}
