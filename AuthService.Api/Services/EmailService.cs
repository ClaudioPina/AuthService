using System.Net;
using Resend;

namespace AuthService.Api.Services
{
    /// <summary>
    /// Implementacion real del servicio de email usando Resend SDK v0.2.2.
    /// Configuracion requerida en appsettings.json:
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
        /// Envia el correo de verificacion de cuenta.
        /// El link contiene un token unico que expira en 24 horas.
        /// Mientras no tengas dominio propio en Resend, usa FromAddress = "onboarding@resend.dev".
        /// </summary>
        public async Task SendVerificationEmailAsync(string toEmail, string verificationLink)
        {
            var from = $"{_config["Email:FromName"]} <{_config["Email:FromAddress"]}>";

            var message = new EmailMessage
            {
                From = from,
                Subject = "Verifica tu cuenta",
                HtmlBody = BuildEmailLayout(
                    preheader: "Confirma tu correo para activar tu cuenta.",
                    title: "Verifica tu cuenta",
                    intro: "Gracias por registrarte. Para activar tu cuenta, confirma tu correo desde el siguiente boton:",
                    ctaText: "Verificar email",
                    ctaUrl: verificationLink,
                    detailsHtml: "<strong>Vigencia:</strong> este enlace expira en 24 horas.",
                    outro: "Si no creaste esta cuenta, puedes ignorar este mensaje."
                )
            };
            message.To.Add(toEmail);

            await _resend.EmailSendAsync(message);
            _logger.LogInformation("Email de verificacion enviado a {Email}", toEmail);
        }

        /// <summary>
        /// Envia el correo de recuperacion de contrasena.
        /// El link contiene un token unico que expira en 1 hora.
        /// </summary>
        public async Task SendPasswordResetEmailAsync(string toEmail, string resetLink)
        {
            var from = $"{_config["Email:FromName"]} <{_config["Email:FromAddress"]}>";

            var message = new EmailMessage
            {
                From = from,
                Subject = "Recuperacion de contrasena",
                HtmlBody = BuildEmailLayout(
                    preheader: "Recibimos una solicitud para restablecer tu contrasena.",
                    title: "Recuperacion de contrasena",
                    intro: "Usa este boton para crear una nueva contrasena de tu cuenta:",
                    ctaText: "Restablecer contrasena",
                    ctaUrl: resetLink,
                    detailsHtml: "<strong>Vigencia:</strong> este enlace expira en 1 hora.",
                    outro: "Si no solicitaste este cambio, ignora este correo."
                )
            };
            message.To.Add(toEmail);

            await _resend.EmailSendAsync(message);
            _logger.LogInformation("Email de reset de contrasena enviado a {Email}", toEmail);
        }

        public async Task SendPasswordChangeVerificationEmailAsync(string toEmail, string confirmationLink)
        {
            var from = $"{_config["Email:FromName"]} <{_config["Email:FromAddress"]}>";

            var message = new EmailMessage
            {
                From = from,
                Subject = "Confirma el cambio de contrasena",
                HtmlBody = BuildEmailLayout(
                    preheader: "Recibimos una solicitud para cambiar tu contrasena.",
                    title: "Confirma el cambio de contrasena",
                    intro: "Para aplicar la nueva contrasena, confirma esta solicitud desde el siguiente boton:",
                    ctaText: "Confirmar cambio",
                    ctaUrl: confirmationLink,
                    detailsHtml: "<strong>Vigencia:</strong> este enlace expira en 30 minutos.",
                    outro: "Si no solicitaste este cambio, ignora este correo y tu contrasena actual seguira vigente."
                )
            };
            message.To.Add(toEmail);

            await _resend.EmailSendAsync(message);
            _logger.LogInformation("Email de confirmacion de cambio de contrasena enviado a {Email}", toEmail);
        }

        /// <summary>
        /// Notifica al usuario sobre un nuevo inicio de sesion en su cuenta.
        /// Se envia en cada login exitoso para que el usuario detecte accesos no autorizados.
        /// </summary>
        public async Task SendNewLoginNotificationAsync(string toEmail, string ip, string userAgent)
        {
            var from = $"{_config["Email:FromName"]} <{_config["Email:FromAddress"]}>";
            var ipSafe = WebUtility.HtmlEncode(ip);
            var userAgentSafe = WebUtility.HtmlEncode(userAgent);
            var timestamp = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm 'UTC'");

            var message = new EmailMessage
            {
                From = from,
                Subject = "Nuevo inicio de sesion en tu cuenta",
                HtmlBody = BuildEmailLayout(
                    preheader: "Se detecto un nuevo acceso a tu cuenta.",
                    title: "Nuevo inicio de sesion detectado",
                    intro: "Registramos un acceso con los siguientes datos:",
                    detailsHtml: $"""
                        <strong>IP:</strong> {ipSafe}<br/>
                        <strong>Dispositivo:</strong> {userAgentSafe}<br/>
                        <strong>Fecha:</strong> {timestamp}
                        """,
                    outro: "Si no reconoces este acceso, cambia tu contrasena de inmediato."
                )
            };
            message.To.Add(toEmail);

            await _resend.EmailSendAsync(message);
            _logger.LogInformation("Notificacion de login enviada a {Email}", toEmail);
        }

        /// <summary>
        /// Notifica al usuario que su contrasena fue cambiada.
        /// Util para detectar cambios no autorizados.
        /// </summary>
        public async Task SendPasswordChangedNotificationAsync(string toEmail)
        {
            var from = $"{_config["Email:FromName"]} <{_config["Email:FromAddress"]}>";
            var timestamp = DateTime.UtcNow.ToString("dd/MM/yyyy HH:mm 'UTC'");

            var message = new EmailMessage
            {
                From = from,
                Subject = "Tu contrasena ha sido cambiada",
                HtmlBody = BuildEmailLayout(
                    preheader: "Tu contrasena fue actualizada correctamente.",
                    title: "Contrasena actualizada",
                    intro: $"Tu contrasena fue cambiada el {timestamp}.",
                    detailsHtml: "Por seguridad, todas tus sesiones activas fueron cerradas.",
                    outro: "Si no realizaste este cambio, contacta al soporte de inmediato."
                )
            };
            message.To.Add(toEmail);

            await _resend.EmailSendAsync(message);
            _logger.LogInformation("Notificacion de cambio de contrasena enviada a {Email}", toEmail);
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
