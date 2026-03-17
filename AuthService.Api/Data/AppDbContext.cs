using Npgsql;
using Polly;
using Polly.Retry;

namespace AuthService.Api.Data
{
    public class AppDbContext
    {
        private readonly IConfiguration _config;
        private readonly AsyncRetryPolicy _retryPolicy;

        public AppDbContext(IConfiguration config)
        {
            _config = config;

            // Retry automático ante errores típicos de red en PostgreSQL
            _retryPolicy = Policy
                .Handle<PostgresException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(300 * attempt)
                );
        }

        /// <summary>
        /// Entrega una conexión PostgreSQL lista para usarse.
        /// No la deja abierta — eso se maneja en los repositorios.
        /// </summary>
        public NpgsqlConnection CreateConnection()
        {
            return new NpgsqlConnection(_config.GetConnectionString("PostgresDb"));
        }

        /// <summary>
        /// Obtiene una conexión abierta con retry automático.
        /// </summary>
        public async Task<NpgsqlConnection> GetOpenConnectionAsync()
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                var conn = CreateConnection();
                await conn.OpenAsync();
                return conn;
            });
        }
    }
}
