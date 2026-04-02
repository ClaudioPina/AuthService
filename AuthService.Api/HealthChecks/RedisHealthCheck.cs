using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthService.Api.HealthChecks
{
    /// <summary>
    /// Verifica que Redis responde correctamente mediante un ping via IDistributedCache.
    /// Si Redis no está configurado (fallback a MemoryCache), reporta Healthy con nota
    /// para no generar falsas alarmas en entornos sin Redis.
    /// Implementación custom sin paquetes externos.
    /// </summary>
    public class RedisHealthCheck : IHealthCheck
    {
        private readonly IDistributedCache _cache;
        private readonly IConfiguration    _config;

        public RedisHealthCheck(IDistributedCache cache, IConfiguration config)
        {
            _cache  = cache;
            _config = config;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var redisConn = _config["Redis:ConnectionString"];
            if (string.IsNullOrWhiteSpace(redisConn))
                return HealthCheckResult.Healthy("Redis no configurado — usando MemoryCache.");

            try
            {
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5)
                };
                await _cache.SetStringAsync("health:ping", "ok", options, cancellationToken);
                var value = await _cache.GetStringAsync("health:ping", cancellationToken);
                return value == "ok"
                    ? HealthCheckResult.Healthy()
                    : HealthCheckResult.Degraded("Redis respondió con valor inesperado.");
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("No se puede conectar a Redis.", ex);
            }
        }
    }
}
