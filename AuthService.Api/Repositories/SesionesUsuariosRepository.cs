using AuthService.Api.Data;
using AuthService.Api.Models;
using AuthService.Api.Utils;
using Npgsql;

namespace AuthService.Api.Repositories
{
    /// <summary>
    /// Repositorio de sesiones: maneja el ciclo de vida de las sesiones activas.
    /// Cada sesión está vinculada a un refresh token almacenado como hash SHA-256.
    /// </summary>
    public class SesionesUsuariosRepository
    {
        private readonly AppDbContext _db;

        public SesionesUsuariosRepository(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Crea una nueva sesión activa en la BD.
        /// IMPORTANTE: recibe el refresh token en texto PLANO y lo hashea internamente.
        /// Los callers NUNCA deben pre-hashear el token antes de llamar este método.
        /// Retorna el ID de la sesión creada.
        /// </summary>
        public async Task<long> CrearSesionAsync(
            long idUsuario,
            string refreshTokenPlano,
            DateTime expiraEn,
            string? userAgent,
            string? ipOrigen)
        {
            // Hasheamos aquí dentro del repositorio. Así solo existe un lugar
            // donde se hashea, evitando el bug de double-hashing.
            var tokenHash = TokenGenerator.HashToken(refreshTokenPlano);

            const string sql = @"
                INSERT INTO SESIONES_USUARIOS
                    (id_usuario, token_refresh, expira_en, user_agent, ip_origen, creacion, estado)
                VALUES
                    (@p_id_usuario, @p_token, @p_expira_en, @p_user_agent, @p_ip, CURRENT_TIMESTAMP, 1)
                RETURNING id_sesion";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_id_usuario", idUsuario);
            cmd.Parameters.AddWithValue("p_token",      tokenHash);
            cmd.Parameters.AddWithValue("p_expira_en",  expiraEn);
            cmd.Parameters.AddWithValue("p_user_agent", (object?)userAgent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("p_ip",         (object?)ipOrigen  ?? DBNull.Value);

            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        /// <summary>
        /// Busca una sesión por hash sin filtrar por estado.
        /// Se usa para detectar reutilización de refresh tokens: si el hash existe
        /// con estado=0, significa que el token ya fue usado/revocado y alguien
        /// lo está intentando usar de nuevo (posible robo).
        /// </summary>
        public async Task<SesionUsuario?> ObtenerSesionPorHashAsync(string tokenHash)
        {
            const string sql = @"
                SELECT id_sesion, id_usuario, token_refresh, expira_en, estado
                FROM SESIONES_USUARIOS
                WHERE token_refresh = @p_hash";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_hash", tokenHash);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new SesionUsuario
            {
                IdSesion  = reader.GetInt64(0),
                IdUsuario = reader.GetInt64(1),
                TokenHash = reader.GetString(2),
                ExpiraEn  = reader.GetDateTime(3),
                Estado    = reader.GetInt32(4)
            };
        }

        /// <summary>
        /// Busca una sesión activa por el hash del refresh token.
        /// El caller debe hashear el token recibido antes de llamar este método.
        /// </summary>
        public async Task<SesionUsuario?> ObtenerSesionActivaPorHashAsync(string tokenHash)
        {
            const string sql = @"
                SELECT id_sesion, id_usuario, token_refresh, expira_en, estado
                FROM SESIONES_USUARIOS
                WHERE token_refresh = @p_hash AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_hash", tokenHash);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new SesionUsuario
            {
                IdSesion  = reader.GetInt64(0),
                IdUsuario = reader.GetInt64(1),
                TokenHash = reader.GetString(2),
                ExpiraEn  = reader.GetDateTime(3),
                Estado    = reader.GetInt32(4)
            };
        }

        /// <summary>
        /// Busca una sesión por su ID (sin filtrar por estado).
        /// Se usa para verificar a qué usuario pertenece antes de revocarla.
        /// </summary>
        public async Task<SesionUsuario?> ObtenerSesionPorIdAsync(long idSesion)
        {
            const string sql = @"
                SELECT id_sesion, id_usuario, token_refresh, expira_en,
                       user_agent, ip_origen, estado, creacion
                FROM SESIONES_USUARIOS
                WHERE id_sesion = @p_id";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_id", idSesion);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new SesionUsuario
            {
                IdSesion  = reader.GetInt64(0),
                IdUsuario = reader.GetInt64(1),
                TokenHash = reader.GetString(2),
                ExpiraEn  = reader.GetDateTime(3),
                UserAgent = reader.IsDBNull(4) ? null : reader.GetString(4),
                IpOrigen  = reader.IsDBNull(5) ? null : reader.GetString(5),
                Estado    = reader.GetInt32(6),
                Creacion  = reader.GetDateTime(7)
            };
        }

        /// <summary>
        /// Retorna todas las sesiones activas de un usuario, ordenadas por más reciente.
        /// </summary>
        public async Task<List<SesionUsuario>> ObtenerSesionesActivasPorUsuarioAsync(long idUsuario)
        {
            const string sql = @"
                SELECT id_sesion, id_usuario, token_refresh, expira_en,
                       user_agent, ip_origen, estado, creacion
                FROM SESIONES_USUARIOS
                WHERE id_usuario = @p_id AND estado = 1
                ORDER BY creacion DESC";

            var lista = new List<SesionUsuario>();
            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_id", idUsuario);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                lista.Add(new SesionUsuario
                {
                    IdSesion  = reader.GetInt64(0),
                    IdUsuario = reader.GetInt64(1),
                    TokenHash = reader.GetString(2),
                    ExpiraEn  = reader.GetDateTime(3),
                    UserAgent = reader.IsDBNull(4) ? null : reader.GetString(4),
                    IpOrigen  = reader.IsDBNull(5) ? null : reader.GetString(5),
                    Estado    = reader.GetInt32(6),
                    Creacion  = reader.GetDateTime(7)
                });
            }
            return lista;
        }

        /// <summary>
        /// Invalida una sesión específica por ID (estado = 0).
        /// Retorna true si se modificó alguna fila.
        /// </summary>
        public async Task<bool> InvalidarSesionPorIdAsync(long idSesion)
        {
            const string sql = @"
                UPDATE SESIONES_USUARIOS SET estado = 0
                WHERE id_sesion = @p_id AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_id", idSesion);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        /// <summary>
        /// Invalida una sesión por el hash de su refresh token.
        /// Se usa durante el refresh (rotación de tokens).
        /// </summary>
        public async Task<bool> InvalidarSesionPorHashAsync(string tokenHash)
        {
            const string sql = @"
                UPDATE SESIONES_USUARIOS SET estado = 0
                WHERE token_refresh = @p_hash AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_hash", tokenHash);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        /// <summary>
        /// Overload transaccional de InvalidarSesionPorHashAsync.
        /// Usa la conexión y transacción compartidas provistas por el caller.
        /// </summary>
        public async Task<bool> InvalidarSesionPorHashAsync(string tokenHash, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            const string sql = @"
                UPDATE SESIONES_USUARIOS SET estado = 0
                WHERE token_refresh = @p_hash AND estado = 1";

            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("p_hash", tokenHash);
            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        /// <summary>
        /// Invalida TODAS las sesiones activas de un usuario.
        /// Se usa en logout-all y change-password.
        /// Retorna la cantidad de sesiones invalidadas.
        /// </summary>
        public async Task<int> InvalidarTodasPorUsuarioAsync(long idUsuario)
        {
            const string sql = @"
                UPDATE SESIONES_USUARIOS SET estado = 0
                WHERE id_usuario = @p_id AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_id", idUsuario);
            return await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Overload transaccional de CrearSesionAsync.
        /// Usa la conexión y transacción compartidas provistas por el caller.
        /// IMPORTANTE: recibe el refresh token en texto PLANO y lo hashea internamente.
        /// </summary>
        public async Task<long> CrearSesionAsync(
            long idUsuario,
            string refreshTokenPlano,
            DateTime expiraEn,
            string? userAgent,
            string? ipOrigen,
            NpgsqlConnection conn,
            NpgsqlTransaction tx)
        {
            var tokenHash = TokenGenerator.HashToken(refreshTokenPlano);

            const string sql = @"
                INSERT INTO SESIONES_USUARIOS
                    (id_usuario, token_refresh, expira_en, user_agent, ip_origen, creacion, estado)
                VALUES
                    (@p_id_usuario, @p_token, @p_expira_en, @p_user_agent, @p_ip, CURRENT_TIMESTAMP, 1)
                RETURNING id_sesion";

            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("p_id_usuario", idUsuario);
            cmd.Parameters.AddWithValue("p_token",      tokenHash);
            cmd.Parameters.AddWithValue("p_expira_en",  expiraEn);
            cmd.Parameters.AddWithValue("p_user_agent", (object?)userAgent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("p_ip",         (object?)ipOrigen  ?? DBNull.Value);

            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        /// <summary>
        /// Invalida todas las sesiones cuya fecha de expiración ya pasó.
        /// Se llama periódicamente desde CleanupExpiredTokensService.
        /// Retorna la cantidad de sesiones invalidadas.
        /// </summary>
        public async Task<int> InvalidarSesionesExpiradasAsync()
        {
            const string sql = @"
                UPDATE SESIONES_USUARIOS SET estado = 0
                WHERE expira_en < CURRENT_TIMESTAMP
                AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            return await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Limita a N sesiones activas por usuario: desactiva las más antiguas
        /// cuando se supera el máximo. Se llama después de crear una sesión nueva.
        /// Retorna los IDs de sesiones desactivadas para que el caller invalide su cache.
        /// </summary>
        public async Task<List<long>> LimitarSesionesActivasAsync(long idUsuario, int maxSesiones)
        {
            // RETURNING id_sesion permite obtener los IDs afectados para limpiar el cache
            // distribuido antes de que expire el TTL de 5 minutos.
            const string sql = @"
                UPDATE SESIONES_USUARIOS SET estado = 0
                WHERE id_sesion IN (
                    SELECT id_sesion FROM (
                        SELECT id_sesion,
                               ROW_NUMBER() OVER (ORDER BY creacion DESC) AS rn
                        FROM SESIONES_USUARIOS
                        WHERE id_usuario = @p_id AND estado = 1
                    ) AS sub
                    WHERE rn > @p_max
                )
                RETURNING id_sesion";

            var ids = new List<long>();
            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_id",  idUsuario);
            cmd.Parameters.AddWithValue("p_max", maxSesiones);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                ids.Add(reader.GetInt64(0));

            return ids;
        }
    }
}
