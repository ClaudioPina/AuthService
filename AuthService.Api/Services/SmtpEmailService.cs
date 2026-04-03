using System.Net;
using System.Net.Mail;

namespace AuthService.Api.Services
{
    /// <summary>
    /// Implementacion de IEmailService usando SMTP estandar.
    /// Util para desarrollo local con herramientas como MailHog o Mailtrap,
    /// que no requieren API keys externas.
    ///
    /// Configuracion requerida en appsettings.json:
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
            var body = BuildEmailLayout(
                preheader: "Confirma tu correo para activar tu cuenta.",
                title: "Verifica tu cuenta",
                intro: "Gracias por registrarte. Para activar tu cuenta, confirma tu correo desde el siguiente boton:",
                ctaText: "Verificar email",
                ctaUrl: verificationLink,
                detailsHtml: "<strong>Vigencia:</strong> este enlace expira en 24 horas.",
                outro: "Si no creaste esta cuenta, puedes ignorar este mensaje."
            );

            await SendAsync(toEmail, subject, body);
            _logger.LogInformation("Email de verificacion enviado a {Email}", toEmail);
        }

        public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
        {
            var subject = "Recuperacion de contrasena";
            var body = BuildEmailLayout(
                preheader: "Recibimos una solicitud para restablecer tu contrasena.",
                title: "Recuperacion de contrasena",
                intro: "Usa este boton para crear una nueva contrasena de tu cuenta:",
                ctaText: "Restablecer contrasena",
                ctaUrl: resetLink,
                detailsHtml: "<strong>Vigencia:</strong> este enlace expira en 1 hora.",
                outro: "Si no solicitaste este cambio, ignora este correo."
            );

            await SendAsync(toEmail, subject, body);
            _logger.LogInformation("Email de reset enviado a {Email}", toEmail);
        }

        public async Task SendPasswordChangeVerificationEmailAsync(string toEmail, string confirmationLink)
        {
            var subject = "Confirma el cambio de contrasena";
            var body = BuildEmailLayout(
                preheader: "Recibimos una solicitud para cambiar tu contrasena.",
                title: "Confirma el cambio de contrasena",
                intro: "Para aplicar la nueva contrasena, confirma esta solicitud desde el siguiente boton:",
                ctaText: "Confirmar cambio",
                ctaUrl: confirmationLink,
                detailsHtml: "<strong>Vigencia:</strong> este enlace expira en 30 minutos.",
                outro: "Si no solicitaste este cambio, ignora este correo y tu contrasena actual seguira vigente."
            );

            await SendAsync(toEmail, subject, body);
            _logger.LogInformation("Email de confirmacion de cambio de contrasena enviado a {Email}", toEmail);
        }

        public async Task SendNewLoginNotificationAsync(string toEmail, string ip, string userAgent)
        {
            var subject = "Nuevo inicio de sesion en tu cuenta";
            var ipSafe = WebUtility.HtmlEncode(ip);
            var userAgentSafe = WebUtility.HtmlEncode(userAgent);
            var timestamp = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm 'UTC'");
            var body = BuildEmailLayout(
                preheader: "Se detecto un nuevo acceso a tu cuenta.",
                title: "Nuevo inicio de sesion detectado",
                intro: "Registramos un acceso con los siguientes datos:",
                detailsHtml: $"""
                    <strong>IP:</strong> {ipSafe}<br/>
                    <strong>Dispositivo:</strong> {userAgentSafe}<br/>
                    <strong>Fecha:</strong> {timestamp}
                    """,
                outro: "Si no reconoces este acceso, cambia tu contrasena de inmediato."
            );

            await SendAsync(toEmail, subject, body);
            _logger.LogInformation("Notificacion de login enviada a {Email}", toEmail);
        }

        public async Task SendPasswordChangedNotificationAsync(string toEmail)
        {
            var subject = "Tu contrasena ha sido cambiada";
            var timestamp = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm 'UTC'");
            var body = BuildEmailLayout(
                preheader: "Tu contrasena fue actualizada correctamente.",
                title: "Contrasena actualizada",
                intro: $"Tu contrasena fue cambiada el {timestamp}.",
                detailsHtml: "Por seguridad, todas tus sesiones activas fueron cerradas.",
                outro: "Si no realizaste este cambio, contacta al soporte de inmediato."
            );

            await SendAsync(toEmail, subject, body);
            _logger.LogInformation("Notificacion de cambio de contrasena enviada a {Email}", toEmail);
        }

        /// <summary>
        /// Envia el email usando SmtpClient. Crea el cliente en cada envio para
        /// evitar problemas con conexiones reutilizadas en long-running services.
        /// </summary>
        private async Task SendAsync(string toEmail, string subject, string htmlBody)
        {
            var host = _config["Email:Smtp:Host"]!;
            var port = _config.GetValue<int>("Email:Smtp:Port", 25);
            var enableSsl = _config.GetValue<bool>("Email:Smtp:EnableSsl", false);
            var user = _config["Email:Smtp:User"];
            var password = _config["Email:Smtp:Password"];
            var from = $"{_config["Email:FromName"]} <{_config["Email:FromAddress"]}>";

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
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

        private string BuildEmailLayout(
            string preheader,
            string title,
            string intro,
            string? ctaText = null,
            string? ctaUrl = null,
            string? detailsHtml = null,
            string? outro = null)
        {
            var brand = WebUtility.HtmlEncode(_config["Email:FromName"] ?? "AuthService");
            var year = DateTime.UtcNow.Year;
            var ctaHtml = string.Empty;
            var linkFallbackHtml = string.Empty;

            if (!string.IsNullOrWhiteSpace(ctaText) && !string.IsNullOrWhiteSpace(ctaUrl))
            {
                var safeUrl = WebUtility.HtmlEncode(ctaUrl);
                var safeText = WebUtility.HtmlEncode(ctaText);
                ctaHtml = $"""
                    <p style="margin:20px 0 0;">
                      <a href="{safeUrl}" style="background:#57b6ff;color:#041321;text-decoration:none;padding:12px 20px;border-radius:10px;display:inline-block;font-weight:700;">
                        {safeText}
                      </a>
                    </p>
                    """;
                linkFallbackHtml = $"""
                    <p style="font-size:12px;color:#9ab3cc;margin:16px 0 0;">
                      Si el boton no funciona, copia y pega este enlace en tu navegador:<br/>
                      <a href="{safeUrl}" style="color:#8ed7ff;word-break:break-all;">{safeUrl}</a>
                    </p>
                    """;
            }

            var detailsBlock = string.IsNullOrWhiteSpace(detailsHtml)
                ? string.Empty
                : $"""
                    <div style="margin-top:18px;padding:12px;border-radius:10px;background:#0b1524;border:1px solid #22395a;color:#d6e6fb;">
                      {detailsHtml}
                    </div>
                    """;

            var outroBlock = string.IsNullOrWhiteSpace(outro)
                ? string.Empty
                : $"<p style=\"margin-top:18px;color:#9ab3cc;\">{WebUtility.HtmlEncode(outro)}</p>";

            return $"""
                <!doctype html>
                <html lang="es">
                <head>
                  <meta charset="utf-8" />
                  <meta name="viewport" content="width=device-width, initial-scale=1" />
                  <title>{WebUtility.HtmlEncode(title)}</title>
                </head>
                <body style="margin:0;padding:0;background:#02040a;font-family:Segoe UI,Arial,sans-serif;color:#f0f6ff;">
                  <div style="display:none;max-height:0;overflow:hidden;opacity:0;">{WebUtility.HtmlEncode(preheader)}</div>
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="padding:24px 0;background:#02040a;">
                    <tr>
                      <td align="center">
                        <table role="presentation" width="620" cellspacing="0" cellpadding="0" style="max-width:620px;width:100%;background:#07111f;border:1px solid #243d61;border-radius:18px;overflow:hidden;">
                          <tr>
                            <td style="background:#0e1f36;padding:18px 24px;color:#ffffff;border-bottom:1px solid #22395a;">
                              <p style="margin:0 0 8px;font-size:11px;letter-spacing:0.12em;text-transform:uppercase;color:#9ab3cc;">AuthService Security</p>
                              <h1 style="margin:0;font-size:22px;color:#f0f6ff;">{brand}</h1>
                            </td>
                          </tr>
                          <tr>
                            <td style="padding:24px;">
                              <h2 style="margin:0 0 10px;font-size:26px;color:#f0f6ff;line-height:1.2;">{WebUtility.HtmlEncode(title)}</h2>
                              <p style="margin:0;color:#c6d8ef;line-height:1.6;">{WebUtility.HtmlEncode(intro)}</p>
                              {ctaHtml}
                              {linkFallbackHtml}
                              {detailsBlock}
                              {outroBlock}
                            </td>
                          </tr>
                          <tr>
                            <td style="padding:14px 24px;background:#0b1524;border-top:1px solid #22395a;font-size:12px;color:#8fa2bf;">
                              &copy; {year} {brand}. Este correo fue generado automaticamente.
                            </td>
                          </tr>
                        </table>
                      </td>
                    </tr>
                  </table>
                </body>
                </html>
                """;
        }
    }
}
