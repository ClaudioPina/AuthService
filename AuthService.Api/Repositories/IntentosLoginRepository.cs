using AuthService.Api.Data;
using AuthService.Api.Models;
using Npgsql;

namespace AuthService.Api.Repositories
{
    /// <summary>
    /// Repositorio para el control de intentos fallidos de login.
    /// Implementa account lockout temporal: después de N intentos fallidos
    /// desde el mismo email o IP, se bloquea por un tiempo configurable.
    ///
    /// Estrategia de bloqueo: se bloquea por EMAIL (no solo por IP) para
    /// evitar que un atacante distribuido evada el lockout cambiando IPs.
    /// </summary>
    public class IntentosLoginRepository
    {
        private readonly AppDbContext _db;

        public IntentosLoginRepository(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Verifica si el email está actualmente bloqueado.
        /// Retorna la fecha de desbloqueo si está bloqueado, null si no.
        /// </summary>
        public async Task<DateTime?> ObtenerBloqueoActivoAsync(string email)
        {
            const string sql = @"
                SELECT bloqueado_hasta
                FROM INTENTOS_LOGIN
                WHERE LOWER(email) = LOWER(@p_email)
                AND bloqueado_hasta > CURRENT_TIMESTAMP
                ORDER BY bloqueado_hasta DESC
                LIMIT 1";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_email", email);

            var result = await cmd.ExecuteScalarAsync();
            return result is DBNull || result is null ? null : (DateTime?)Convert.ToDateTime(result);
        }

        /// <summary>
        /// Registra un intento fallido de login.
        /// Usa un único UPSERT atómico sobre el índice único LOWER(email).
        /// Si ya existe fila para ese email, incrementa el contador; si no, inserta.
        /// El bloqueo se activa cuando el nuevo contador alcanza el máximo.
        /// El email se normaliza a minúsculas en el INSERT para consistencia.
        /// </summary>
        public async Task RegistrarIntentoFallidoAsync(string email, string ip, int maxIntentos, int minutoBloqueo)
        {
            // ON CONFLICT (LOWER(email)) requiere el índice único funcional definido en script_DB.sql.
            // INTENTOS_LOGIN.columna = valor anterior, EXCLUDED.columna = valor que se intentó insertar.
            const string sql = @"
                INSERT INTO INTENTOS_LOGIN (email, ip_origen, intentos, ultimo_intento, bloqueado_hasta)
                VALUES (LOWER(@p_email), @p_ip, 1, CURRENT_TIMESTAMP, NULL)
                ON CONFLICT (LOWER(email)) DO UPDATE
                SET intentos        = INTENTOS_LOGIN.intentos + 1,
                    ultimo_intento   = CURRENT_TIMESTAMP,
                    ip_origen        = EXCLUDED.ip_origen,
                    bloqueado_hasta  = CASE
                        WHEN INTENTOS_LOGIN.intentos + 1 >= @p_max
                        THEN CURRENT_TIMESTAMP + (@p_minutos || ' minutes')::INTERVAL
                        ELSE INTENTOS_LOGIN.bloqueado_hasta
                    END";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_email",   email);
            cmd.Parameters.AddWithValue("p_ip",      ip);
            cmd.Parameters.AddWithValue("p_max",     maxIntentos);
            cmd.Parameters.AddWithValue("p_minutos", minutoBloqueo);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Limpia el registro de intentos fallidos tras un login exitoso.
        /// Así el usuario comienza de cero la próxima vez.
        /// </summary>
        public async Task LimpiarIntentosAsync(string email)
        {
            const string sql = @"
                DELETE FROM INTENTOS_LOGIN
                WHERE LOWER(email) = LOWER(@p_email)";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_email", email);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Elimina registros de intentos cuyo último intento sea antiguo.
        /// Se llama desde CleanupExpiredTokensService para evitar acumulación.
        /// </summary>
        public async Task<int> LimpiarIntentosAntigousAsync(int horasAntiguedad = 24)
        {
            const string sql = @"
                DELETE FROM INTENTOS_LOGIN
                WHERE ultimo_intento < CURRENT_TIMESTAMP - (@p_horas || ' hours')::INTERVAL";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_horas", horasAntiguedad);
            return await cmd.ExecuteNonQueryAsync();
        }
    }
}
