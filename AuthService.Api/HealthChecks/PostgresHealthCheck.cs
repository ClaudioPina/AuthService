using AuthService.Api.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace AuthService.Api.HealthChecks
{
    /// <summary>
    /// Verifica conectividad con PostgreSQL ejecutando "SELECT 1".
    /// Implementación custom sin paquetes externos — usa AppDbContext ya registrado en DI.
    /// </summary>
    public class PostgresHealthCheck : IHealthCheck
    {
        private readonly AppDbContext _db;

        public PostgresHealthCheck(AppDbContext db) => _db = db;

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var conn = await _db.GetOpenConnectionAsync();
                using var cmd  = new NpgsqlCommand("SELECT 1", conn);
                await cmd.ExecuteScalarAsync(cancellationToken);
                return HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                return HealthCheckResult.Unhealthy("No se puede conectar a PostgreSQL.", ex);
            }
        }
    }
}
