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

            auth.MapGet("/verify-email/{token}", async (string token, IAutenticacionService svc) =>
                await svc.VerifyEmailAsync(token))
                .WithName("VerifyEmail")
                .WithOpenApi();

            auth.MapPost("/forgot-password", async (ForgotPasswordRequest req, IAutenticacionService svc) =>
                await svc.ForgotPasswordAsync(req))
                .WithName("ForgotPassword")
                .WithOpenApi()
                .RequireRateLimiting("forgotpassword-policy");

            auth.MapPost("/reset-password", async (ResetPasswordRequest req, IAutenticacionService svc) =>
                await svc.ResetPasswordAsync(req))
                .WithName("ResetPassword")
                .WithOpenApi();

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

            // ── Rutas protegidas (requieren JWT válido) ─────────────────────────

            auth.MapPost("/change-password", async (
                ChangePasswordRequest req,
                IAutenticacionService svc,
                HttpContext ctx) =>
                await svc.ChangePasswordAsync(req, GetUserId(ctx)))
                .RequireAuthorization()
                .WithName("ChangePassword")
                .WithOpenApi();

            auth.MapPost("/logout", async (IAutenticacionService svc, HttpContext ctx) =>
                await svc.LogoutAsync(GetSessionId(ctx)))
                .RequireAuthorization()
                .WithOpenApi();

            auth.MapPost("/logout-all", async (IAutenticacionService svc, HttpContext ctx) =>
                await svc.LogoutAllAsync(GetUserId(ctx)))
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
                await svc.RevokeSessionAsync(idSesion, GetUserId(ctx)))
                .RequireAuthorization()
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
    }
}
