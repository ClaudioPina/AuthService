using AuthService.Api.Repositories;
using Microsoft.Extensions.Caching.Distributed;

namespace AuthService.Api.Middlewares
{
    public class ValidarSesionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IDistributedCache _cache;
        private readonly ILogger<ValidarSesionMiddleware> _logger;

        // TTL del cache menor que la vida del JWT (15 min) para limitar la ventana de stale.
        // Si una sesión se invalida, el peor caso es que el cache la sirva como válida 5 min más.
        // Las operaciones críticas (logout, revoke, change-password) invalidan el cache explícitamente.
        private static readonly DistributedCacheEntryOptions CacheOptions = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        };

        public ValidarSesionMiddleware(
            RequestDelegate next,
            IDistributedCache cache,
            ILogger<ValidarSesionMiddleware> logger)
        {
            _next   = next;
            _cache  = cache;
            _logger = logger;
        }

        public async Task InvokeAsync(
            HttpContext context,
            SesionesUsuariosRepository sesionesRepo)
        {
            // Si el usuario NO está autenticado, no hay sesión que validar
            if (!context.User.Identity?.IsAuthenticated ?? true)
            {
                await _next(context);
                return;
            }

            var sesionIdClaim = context.User.Claims
                .FirstOrDefault(c => c.Type == "id_sesion");

            if (sesionIdClaim == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { message = "Sesión inválida." });
                return;
            }

            var idSesion = long.Parse(sesionIdClaim.Value);
            var cacheKey = $"sess:{idSesion}";

            // 1. Intentar leer del cache. Si Redis no está disponible, continuar hacia la BD.
            string? cached = null;
            try
            {
                cached = await _cache.GetStringAsync(cacheKey);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Cache no disponible al validar sesión {IdSesion}. Consultando BD.", idSesion);
            }

            if (cached == "1")
            {
                // Cache hit: sesión válida — no necesita consulta a BD
                await _next(context);
                return;
            }

            if (cached == "0")
            {
                // Cache hit: sesión ya invalidada
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "La sesión ya no es válida. Debes iniciar sesión nuevamente."
                });
                return;
            }

            // 2. Cache miss: consultar BD
            var sesion = await sesionesRepo.ObtenerSesionPorIdAsync(idSesion);

            if (sesion == null || sesion.Estado == 0)
            {
                // Cachear el resultado negativo para evitar queries repetidas de sesiones inválidas
                try { await _cache.SetStringAsync(cacheKey, "0", CacheOptions); }
                catch { /* ignorar fallos del cache — la seguridad no depende de él */ }

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "La sesión ya no es válida. Debes iniciar sesión nuevamente."
                });
                return;
            }

            // Cachear el resultado positivo
            try { await _cache.SetStringAsync(cacheKey, "1", CacheOptions); }
            catch { /* ignorar fallos del cache */ }

            await _next(context);
        }
    }
}
