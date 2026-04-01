using AuthService.Api.Repositories;

namespace AuthService.Api.Services
{
    /// <summary>
    /// Servicio en background que limpia registros expirados de la BD cada hora.
    /// Sin esto, tokens de verificación, reset de contraseña y sesiones expiradas
    /// se acumulan indefinidamente en las tablas.
    ///
    /// BackgroundService es la clase base de .NET para tareas de larga duración
    /// que corren en paralelo al servidor HTTP, sin bloquear requests.
    ///
    /// Los repositorios son Scoped (una instancia por request), pero un BackgroundService
    /// es Singleton. Para resolver esto se usa IServiceScopeFactory: crea un scope
    /// temporal por cada ejecución, obtiene los repositorios de ese scope, y lo destruye
    /// al terminar. Es el patrón estándar de .NET para este caso.
    /// </summary>
    public class CleanupExpiredTokensService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CleanupExpiredTokensService> _logger;

        // Intervalo entre ejecuciones — 1 hora es suficiente para tokens de este sistema
        private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

        public CleanupExpiredTokensService(
            IServiceScopeFactory scopeFactory,
            ILogger<CleanupExpiredTokensService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger       = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CleanupExpiredTokensService iniciado.");

            // El loop corre mientras la app esté activa. stoppingToken se cancela
            // cuando el host hace shutdown, lo que interrumpe el Task.Delay y sale limpio.
            while (!stoppingToken.IsCancellationRequested)
            {
                await CleanupAsync();
                await Task.Delay(Interval, stoppingToken);
            }

            _logger.LogInformation("CleanupExpiredTokensService detenido.");
        }

        private async Task CleanupAsync()
        {
            try
            {
                // Crear un scope temporal para obtener los repositorios Scoped
                using var scope = _scopeFactory.CreateScope();

                var resetRepo    = scope.ServiceProvider.GetRequiredService<ResetPasswordRepository>();
                var verifRepo    = scope.ServiceProvider.GetRequiredService<VerificacionEmailRepository>();
                var sesionRepo   = scope.ServiceProvider.GetRequiredService<SesionesUsuariosRepository>();
                var intentosRepo = scope.ServiceProvider.GetRequiredService<IntentosLoginRepository>();

                var resetCount    = await resetRepo.InvalidarTokensExpiradosAsync();
                var verifCount    = await verifRepo.InvalidarTokensExpiradosAsync();
                var sesionCount   = await sesionRepo.InvalidarSesionesExpiradasAsync();
                var intentosCount = await intentosRepo.LimpiarIntentosAntigousAsync();

                _logger.LogInformation(
                    "Cleanup completado: {Reset} tokens reset, {Verif} tokens verificación, {Sesiones} sesiones, {Intentos} intentos login.",
                    resetCount, verifCount, sesionCount, intentosCount);
            }
            catch (Exception ex)
            {
                // Loguear pero no relanzar — si falla una iteración, la próxima igual corre
                _logger.LogError(ex, "Error durante la limpieza de tokens expirados.");
            }
        }
    }
}
