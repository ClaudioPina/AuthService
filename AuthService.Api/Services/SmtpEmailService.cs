using System.Net;
using System.Net.Mail;

namespace AuthService.Api.Services
{
    /// <summary>
    /// Implementación de IEmailService usando SMTP estándar.
    /// Útil para desarrollo local con herramientas como MailHog o Mailtrap,
    /// que no requieren API keys externas.
    ///
    /// Configuración requerida en appsettings.json:
    ///   Email:FromAddress, Email:FromName
    ///   Email:Smtp:Host, Email:Smtp:Port
    ///   Email:Smtp:User (opcional), Email:Smtp:Password (opcional)
    ///   Email:Smtp:EnableSsl (true/false, default false en dev)
    ///
    /// Para usar MailHog localmente:
    ///   docker run -p 1025:1025 -p 8025:8025 mailhog/mailhog
    ///   Host=localhost, Port=1025, EnableSsl=false (sin credenciales)
    ///   Revisar emails en http://localhost:8025
    /// </summary>
    public class SmtpEmailService : IEmailService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailService> _logger;

        public SmtpEmailService(IConfiguration config, ILogger<SmtpEmailService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendVerificationEmailAsync(string toEmail, string verificationLink)
        {
            var subject = "Verifica tu cuenta";
            var body = $"""
                <h2>Bienvenido a AuthService</h2>
                <p>Haz clic en el siguiente enlace para verificar tu email:</p>
                <a href="{verificationLink}">Verificar email</a>
                <p>Este enlace expira en 24 horas.</p>
                <p>Si no creaste esta cuenta, ignora este correo.</p>
                """;

            await SendAsync(toEmail, subject, body);
            _logger.LogInformation("Email de verificación enviado a {Email}", toEmail);
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
        {
            var subject = "Recuperación de contraseña";
            var body = $"""
                <h2>Recuperación de contraseña</h2>
                <p>Haz clic en el siguiente enlace para restablecer tu contraseña:</p>
                <a href="{resetLink}">Restablecer contraseña</a>
                <p>Este enlace expira en 1 hora.</p>
                <p>Si no solicitaste este cambio, ignora este correo.</p>
                """;

            await SendAsync(toEmail, subject, body);
            _logger.LogInformation("Email de reset enviado a {Email}", toEmail);
        }

        public async Task SendNewLoginNotificationAsync(string toEmail, string ip, string userAgent)
        {
            var subject = "Nuevo inicio de sesión en tu cuenta";
            var body = $"""
                <h2>Nuevo inicio de sesión detectado</h2>
                <p>Se ha iniciado sesión en tu cuenta con los siguientes datos:</p>
                <ul>
                    <li><strong>IP:</strong> {WebUtility.HtmlEncode(ip)}</li>
                    <li><strong>Dispositivo:</strong> {WebUtility.HtmlEncode(userAgent)}</li>
                    <li><strong>Fecha:</strong> {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC</li>
                </ul>
                <p>Si fuiste tú, puedes ignorar este correo.</p>
                <p>Si no fuiste tú, cambia tu contraseña inmediatamente.</p>
                """;

            await SendAsync(toEmail, subject, body);
            _logger.LogInformation("Notificación de login enviada a {Email}", toEmail);
        }

        public async Task SendPasswordChangedNotificationAsync(string toEmail)
        {
            var subject = "Tu contraseña ha sido cambiada";
            var body = $"""
                <h2>Contraseña actualizada</h2>
                <p>Tu contraseña fue cambiada el {DateTime.UtcNow:dd/MM/yyyy HH:mm} UTC.</p>
                <p>Todas tus sesiones activas han sido cerradas por seguridad.</p>
                <p>Si no realizaste este cambio, contacta al soporte inmediatamente.</p>
                """;

            await SendAsync(toEmail, subject, body);
            _logger.LogInformation("Notificación de cambio de contraseña enviada a {Email}", toEmail);
        }

        /// <summary>
        /// Envía el email usando SmtpClient. Crea el cliente en cada envío para
        /// evitar problemas con conexiones reutilizadas en long-running services.
        /// </summary>
        private async Task SendAsync(string toEmail, string subject, string htmlBody)
        {
            var host     = _config["Email:Smtp:Host"]!;
            var port     = _config.GetValue<int>("Email:Smtp:Port", 25);
            var enableSsl = _config.GetValue<bool>("Email:Smtp:EnableSsl", false);
            var user     = _config["Email:Smtp:User"];
            var password = _config["Email:Smtp:Password"];
            var from     = $"{_config["Email:FromName"]} <{_config["Email:FromAddress"]}>";

            using var client = new SmtpClient(host, port)
            {
                EnableSsl   = enableSsl,
                Credentials = !string.IsNullOrEmpty(user)
                    ? new NetworkCredential(user, password)
                    : null
            };

            using var message = new MailMessage(from, toEmail, subject, htmlBody)
            {
                IsBodyHtml = true
            };

            await client.SendMailAsync(message);
        }
    }
}
