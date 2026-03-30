using System.Security.Claims;
using AuthService.Api.Repositories;

namespace AuthService.Api.Middlewares
{
    public class ValidarSesionMiddleware
    {
        private readonly RequestDelegate _next;

        public ValidarSesionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            SesionesUsuariosRepository sesionesRepo)
        {
            // Si el usuario NO está autenticado, seguimos
            if (!context.User.Identity?.IsAuthenticated ?? true)
            {
                await _next(context);
                return;
            }

            // Buscar claim id_sesion
            var sesionIdClaim = context.User.Claims
                .FirstOrDefault(c => c.Type == "id_sesion");

            if (sesionIdClaim == null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Sesión inválida."
                });
                return;
            }

            var idSesion = long.Parse(sesionIdClaim.Value);

            // Buscar sesión en BD
            var sesion = await sesionesRepo.ObtenerSesionPorIdAsync(idSesion);

            if (sesion == null || sesion.Estado == 0)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "La sesión ya no es válida. Debes iniciar sesión nuevamente."
                });
                return;
            }

            // Todo OK → continúa la petición
            await _next(context);
        }
    }
}
