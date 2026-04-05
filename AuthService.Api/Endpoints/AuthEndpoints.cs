using AuthService.Api.Dtos.Auth;
using AuthService.Api.Services;


namespace AuthService.Api.Endpoints
{
    /// <summary>
    /// Define todas las rutas del microservicio de autenticación.
    /// Esta clase SOLO mapea rutas — no tiene lógica de negocio.
    /// Toda la lógica está en AutenticacionService.
    /// </summary>
    public static class AuthEndpoints
    {
        /// <summary>
        /// Registra todas las rutas bajo el prefijo /auth.
        /// Llama a este método desde Program.cs con: app.MapAuthEndpoints()
        /// </summary>
        public static WebApplication MapAuthEndpoints(this WebApplication app)
        {
            var auth = app.MapGroup("/auth");

            // ── Rutas públicas ──────────────────────────────────────────────────

            auth.MapPost("/register", async (RegisterRequest req, IAutenticacionService svc) =>
                await svc.RegisterAsync(req))
                .WithName("RegisterUser")
                .WithOpenApi()
                .RequireRateLimiting("register-policy");

            auth.MapPost("/login", async (
                LoginRequest req,
                IAutenticacionService svc,
                HttpContext ctx) =>
                await svc.LoginAsync(
                    req,
                    ctx.Request.Headers["User-Agent"].ToString(),
                    ctx.Connection.RemoteIpAddress?.ToString()))
                .WithName("LoginUser")
                .WithOpenApi()
                .RequireRateLimiting("login-policy");

            auth.MapGet("/verify-email/{token}", async (
                string token,
                IAutenticacionService svc,
                IConfiguration cfg,
                HttpContext ctx) =>
            {
                var result = await svc.VerifyEmailAsync(token);

                // Si el endpoint se abre desde navegador (link del email), redirigir al login
                // o a una pantalla de resultado. Para clientes API/tests se mantiene JSON.
                if (WantsBrowserNavigation(ctx) && result is IStatusCodeHttpResult statusResult)
                {
                    return Results.Redirect(BuildVerificationRedirectUrl(cfg, statusResult.StatusCode));
                }

                return result;
            })
                .WithName("VerifyEmail")
                .WithOpenApi();

            auth.MapPost("/forgot-password", async (ForgotPasswordRequest req, IAutenticacionService svc) =>
                await svc.ForgotPasswordAsync(req))
                .WithName("ForgotPassword")
                .WithOpenApi()
                .RequireRateLimiting("forgotpassword-policy");

            auth.MapPost("/reset-password", async (ResetPasswordRequest req, IAutenticacionService svc, HttpContext ctx) =>
                await svc.ResetPasswordAsync(
                    req,
                    ctx.Connection.RemoteIpAddress?.ToString(),
                    ctx.Request.Headers["User-Agent"].ToString()))
                .WithName("ResetPassword")
                .WithOpenApi();

            auth.MapGet("/confirm-change-password/{token}", async (string token, IAutenticacionService svc, HttpContext ctx) =>
                await svc.ConfirmPasswordChangeAsync(
                    token,
                    ctx.Connection.RemoteIpAddress?.ToString(),
                    ctx.Request.Headers["User-Agent"].ToString()))
                .WithName("ConfirmChangePassword")
                .WithOpenApi();

            auth.MapPost("/resend-verification", async (ResendVerificationRequest req, IAutenticacionService svc) =>
                await svc.ResendVerificationAsync(req))
                .WithName("ResendVerification")
                .WithOpenApi()
                .RequireRateLimiting("resendverification-policy");

            auth.MapPost("/refresh-token", async (
                RefreshTokenRequest req,
                IAutenticacionService svc,
                HttpContext ctx) =>
                await svc.RefreshTokenAsync(
                    req,
                    ctx.Request.Headers["User-Agent"].ToString(),
                    ctx.Connection.RemoteIpAddress?.ToString()))
                .WithName("RefreshToken")
                .WithOpenApi();

            auth.MapPost("/google", async (
                GoogleLoginRequest req,
                IAutenticacionService svc,
                HttpContext ctx) =>
                await svc.GoogleLoginAsync(
                    req.IdToken,
                    ctx.Request.Headers["User-Agent"].ToString(),
                    ctx.Connection.RemoteIpAddress?.ToString()))
                .WithName("GoogleLogin")
                .WithOpenApi()
                .RequireRateLimiting("login-policy");

            // ── Rutas protegidas (requieren JWT válido) ─────────────────────────

            auth.MapPost("/change-password", async (
                ChangePasswordRequest req,
                IAutenticacionService svc,
                HttpContext ctx) =>
                await svc.ChangePasswordAsync(
                    req,
                    GetUserId(ctx),
                    ctx.Connection.RemoteIpAddress?.ToString(),
                    ctx.Request.Headers["User-Agent"].ToString()))
                .RequireAuthorization()
                .WithName("ChangePassword")
                .WithOpenApi();

            auth.MapPost("/logout", async (IAutenticacionService svc, HttpContext ctx) =>
                await svc.LogoutAsync(GetSessionId(ctx)))
                .RequireAuthorization()
                .WithOpenApi();

            auth.MapPost("/logout-all", async (IAutenticacionService svc, HttpContext ctx) =>
                await svc.LogoutAllAsync(
                    GetUserId(ctx),
                    ctx.Connection.RemoteIpAddress?.ToString(),
                    ctx.Request.Headers["User-Agent"].ToString()))
                .RequireAuthorization()
                .WithOpenApi();

            auth.MapGet("/sessions", async (IAutenticacionService svc, HttpContext ctx) =>
                await svc.GetSessionsAsync(GetUserId(ctx)))
                .RequireAuthorization()
                .WithOpenApi();

            auth.MapPost("/sessions/revoke/{idSesion:long}", async (
                long idSesion,
                IAutenticacionService svc,
                HttpContext ctx) =>
                await svc.RevokeSessionAsync(
                    idSesion,
                    GetUserId(ctx),
                    ctx.Connection.RemoteIpAddress?.ToString(),
                    ctx.Request.Headers["User-Agent"].ToString()))
                .RequireAuthorization()
                .WithOpenApi();

            auth.MapGet("/me", async (IAutenticacionService svc, HttpContext ctx) =>
                await svc.ObtenerPerfilAsync(GetUserId(ctx)))
                .RequireAuthorization()
                .WithName("GetProfile")
                .WithOpenApi();

            return app;
        }

        /// <summary>Extrae el ID de usuario del claim "id" del JWT. Lanza 401 si no existe.</summary>
        private static long GetUserId(HttpContext ctx)
        {
            var claim = ctx.User.Claims.FirstOrDefault(c => c.Type == "id");
            if (claim is null || !long.TryParse(claim.Value, out var id))
                throw new UnauthorizedAccessException("Claim 'id' no encontrado en el JWT.");
            return id;
        }

        /// <summary>Extrae el ID de sesión del claim "id_sesion" del JWT. Lanza 401 si no existe.</summary>
        private static long GetSessionId(HttpContext ctx)
        {
            var claim = ctx.User.Claims.FirstOrDefault(c => c.Type == "id_sesion");
            if (claim is null || !long.TryParse(claim.Value, out var id))
                throw new UnauthorizedAccessException("Claim 'id_sesion' no encontrado en el JWT.");
            return id;
        }

        /// <summary>
        /// Detecta navegación desde navegador/webview de forma robusta.
        /// Algunos clientes de correo no envían "Accept: text/html", por eso
        /// se consideran también headers de navegación y user-agent.
        /// </summary>
        private static bool WantsBrowserNavigation(HttpContext ctx)
        {
            var accept = ctx.Request.Headers.Accept.ToString();
            if (accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                return true;

            var secFetchDest = ctx.Request.Headers["Sec-Fetch-Dest"].ToString();
            if (secFetchDest.Equals("document", StringComparison.OrdinalIgnoreCase))
                return true;

            var secFetchMode = ctx.Request.Headers["Sec-Fetch-Mode"].ToString();
            if (secFetchMode.Equals("navigate", StringComparison.OrdinalIgnoreCase))
                return true;

            var userAgent = ctx.Request.Headers.UserAgent.ToString();
            if (!string.IsNullOrWhiteSpace(userAgent) &&
                userAgent.Contains("Mozilla", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        /// <summary>
        /// URL base de pantalla post-verificación.
        /// Prioriza App:VerificationResultUrl; si no existe, usa App:LoginUrl;
        /// y como último fallback App:BaseUrl + /login.
        /// </summary>
        private static string GetVerificationResultBaseUrl(IConfiguration cfg)
        {
            var configured = cfg["App:VerificationResultUrl"];
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            configured = cfg["App:LoginUrl"];
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            var baseUrl = cfg["App:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
            return $"{baseUrl}/login";
        }

        /// <summary>
        /// Construye la URL de redirección con parámetros de estado para que el frontend
        /// muestre una pantalla amigable de éxito o error.
        /// </summary>
        private static string BuildVerificationRedirectUrl(IConfiguration cfg, int? statusCode)
        {
            var isSuccess = statusCode is >= 200 and < 300;
            var status = isSuccess ? "success" : "error";
            var title = isSuccess ? "Email verificado" : "Verificación fallida";
            var message = isSuccess
                ? "Tu cuenta fue verificada correctamente. Ya puedes iniciar sesión."
                : "No pudimos verificar tu email. El enlace puede haber expirado o ya fue utilizado.";

            var baseUrl = GetVerificationResultBaseUrl(cfg);
            return AppendQueryToUrl(baseUrl, new Dictionary<string, string>
            {
                ["status"] = status,
                ["title"] = title,
                ["message"] = message,
                ["loginUrl"] = GetLoginUrl(cfg)
            });
        }

        /// <summary>
        /// URL del login del frontend para la redirección final.
        /// </summary>
        private static string GetLoginUrl(IConfiguration cfg)
        {
            var configured = cfg["App:LoginUrl"];
            if (!string.IsNullOrWhiteSpace(configured))
                return configured;

            var baseUrl = cfg["App:BaseUrl"]?.TrimEnd('/') ?? string.Empty;
            return $"{baseUrl}/login";
        }

        /// <summary>
        /// Agrega query string a una URL normal o a una URL con hash-router
        /// (ej: http://localhost:5500/#/verify-result).
        /// </summary>
        private static string AppendQueryToUrl(string url, IDictionary<string, string> query)
        {
            var encodedPairs = query
                .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}");
            var queryString = string.Join("&", encodedPairs);
            if (string.IsNullOrWhiteSpace(queryString))
                return url;

            var hashIndex = url.IndexOf('#');
            if (hashIndex >= 0)
            {
                var prefix = url[..(hashIndex + 1)];
                var hashPart = url[(hashIndex + 1)..];
                var separator = hashPart.Contains('?', StringComparison.Ordinal) ? "&" : "?";
                return $"{prefix}{hashPart}{separator}{queryString}";
            }

            var defaultSeparator = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return $"{url}{defaultSeparator}{queryString}";
        }
    }
}
