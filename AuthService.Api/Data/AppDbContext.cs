using Npgsql;
using Polly;
using Polly.Retry;

namespace AuthService.Api.Data
{
    /// <summary>
    /// Gestiona las conexiones a PostgreSQL con retry automático via Polly.
    /// Soporta dos formas de configurar la conexión:
    ///   1. Variable de entorno DATABASE_URL (Fly.io la inyecta automáticamente).
    ///   2. ConnectionStrings:PostgresDb en appsettings.json (desarrollo local).
    /// Npgsql puede parsear URIs postgres:// directamente.
    /// </summary>
    public class AppDbContext
    {
        private readonly IConfiguration _config;
        private readonly AsyncRetryPolicy _retryPolicy;

        public AppDbContext(IConfiguration config)
        {
            _config = config;

            // Reintenta 3 veces ante errores de red, conexión rechazada o errores del servidor PostgreSQL,
            // esperando 300ms, 600ms, 900ms entre intentos.
            _retryPolicy = Policy
                .Handle<NpgsqlException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
                    sleepDurationProvider: attempt => TimeSpan.FromMilliseconds(300 * attempt)
                );
        }

        /// <summary>
        /// Crea una nueva conexión (sin abrirla todavía).
        /// Prioriza DATABASE_URL (Fly.io) sobre el appsettings local.
        /// </summary>
        public NpgsqlConnection CreateConnection()
        {
            var connString = Environment.GetEnvironmentVariable("DATABASE_URL")
                             ?? _config.GetConnectionString("PostgresDb")!;
            return new NpgsqlConnection(connString);
        }

        /// <summary>
        /// Retorna una conexión ya abierta, con retry automático.
        /// Usar siempre con "using var conn = await _db.GetOpenConnectionAsync()".
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

        /// <summary>
        /// Abre una conexión e inicia una transacción explícita.
        /// Usar cuando múltiples operaciones de repositorio deben ser atómicas.
        /// El caller es responsable de hacer CommitAsync(), RollbackAsync() y Dispose
        /// tanto de la conexión como de la transacción.
        /// </summary>
        public async Task<(NpgsqlConnection Connection, NpgsqlTransaction Transaction)> BeginTransactionAsync()
        {
            var conn = await GetOpenConnectionAsync();
            var tx   = await conn.BeginTransactionAsync();
            return (conn, tx);
        }
    }
}
