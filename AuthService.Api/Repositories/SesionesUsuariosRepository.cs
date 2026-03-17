using System.Data;
using AuthService.Api.Data;
using Npgsql;
using AuthService.Api.Utils;
using AuthService.Api.Models;

namespace AuthService.Api.Repositories
{
    public class SesionesUsuariosRepository
    {
        private readonly AppDbContext _db;

        public SesionesUsuariosRepository(AppDbContext db)
        {
            _db = db;
        }

        // Crear una nueva sesión (refresh token)
        public async Task<long> CrearSesionAsync(
            long idUsuario,
            string refreshToken,
            DateTime expiraEn,
            string? userAgent,
            string? ipOrigen)
        {
            const string sql = @"
                INSERT INTO SESIONES_USUARIOS
                    (id_usuario, token_refresh, expira_en,
                    user_agent, ip_origen,
                    propietario, creacion, estado)
                VALUES
                    (@p_id_usuario,
                    @p_token_refresh,
                    @p_expira_en,
                    @p_user_agent,
                    @p_ip_origen,
                    @p_propietario,
                    CURRENT_TIMESTAMP,
                    1)
                RETURNING id_sesion";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);

            var hashedToken = TokenGenerator.HashToken(refreshToken);

            cmd.Parameters.AddWithValue("p_id_usuario", idUsuario);
            cmd.Parameters.AddWithValue("p_token_refresh", hashedToken); // guardar el hash, no el token real
            cmd.Parameters.AddWithValue("p_expira_en", expiraEn);
            cmd.Parameters.AddWithValue("p_user_agent", (object?)userAgent ?? DBNull.Value);
            cmd.Parameters.AddWithValue("p_ip_origen", (object?)ipOrigen ?? DBNull.Value);
            cmd.Parameters.AddWithValue("p_propietario", idUsuario);

            var result = await cmd.ExecuteScalarAsync();

            return Convert.ToInt64(result);
        }

        // Obtener sesión activa por hash del token
        public async Task<SesionUsuario?> ObtenerSesionActivaPorHashAsync(string hashedToken)
        {
            const string sql = @"
                SELECT id_sesion, id_usuario, token_refresh, expira_en, estado
                FROM SESIONES_USUARIOS
                WHERE token_refresh = @p_hash
                AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_hash", hashedToken);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new SesionUsuario
            {
                IdSesion   = reader.GetInt64(0),
                IdUsuario  = reader.GetInt64(1),
                TokenHash  = reader.GetString(2),
                ExpiraEn   = reader.GetDateTime(3),
                Estado     = reader.GetInt32(4)
            };
        }

        // Limitar a N sesiones activas: se desactivan las más antiguas
        public async Task<int> LimitarSesionesActivasAsync(long idUsuario, int maxSesiones)
        {
            const string sql = @"
                UPDATE SESIONES_USUARIOS
                SET estado = 0,
                    actualizacion = CURRENT_TIMESTAMP,
                    usuario = @p_id_usuario
                WHERE id_sesion IN (
                    SELECT id_sesion FROM (
                        SELECT id_sesion,
                            ROW_NUMBER() OVER (ORDER BY creacion DESC) AS rn
                        FROM SESIONES_USUARIOS
                        WHERE id_usuario = @p_id_usuario
                        AND estado = 1
                    ) as subquery
                    WHERE rn > @p_max_sesiones
                )";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_id_usuario", idUsuario);
            cmd.Parameters.AddWithValue("p_max_sesiones", maxSesiones);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows;
        }

        // Invalidar sesión por hash del token
        public async Task<bool> InvalidarSesionPorHashAsync(string hash, long auditor)
        {
            const string sql = @"
                UPDATE SESIONES_USUARIOS
                SET estado = 0,
                    usuario = @p_usuario,
                    actualizacion = CURRENT_TIMESTAMP
                WHERE token_refresh = @p_hash
                AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_usuario", auditor); // o usuario auditor
            cmd.Parameters.AddWithValue("p_hash", hash);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }

        public async Task<List<SesionUsuario>> ObtenerSesionesActivasPorUsuarioAsync(long idUsuario)
        {
            const string sql = @"
                SELECT id_sesion, id_usuario, token_refresh, expira_en, estado,
                    user_agent, ip_origen, propietario, creacion, usuario, actualizacion
                FROM SESIONES_USUARIOS
                WHERE id_usuario = @p_id_usuario
                AND estado = 1
                ORDER BY creacion DESC";

            var lista = new List<SesionUsuario>();

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_id_usuario", idUsuario);

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                lista.Add(new SesionUsuario
                {
                    IdSesion = reader.GetInt64(0),
                    IdUsuario = reader.GetInt64(1),
                    TokenHash = reader.GetString(2),
                    ExpiraEn = reader.GetDateTime(3),
                    Estado = reader.GetInt32(4),
                    UserAgent = reader.IsDBNull(5) ? null : reader.GetString(5),
                    IpOrigen = reader.IsDBNull(6) ? null : reader.GetString(6),
                    Propietario = reader.GetInt64(7),
                    Creacion = reader.GetDateTime(8),
                    UsuarioAuditoria = reader.IsDBNull(9) ? null : reader.GetInt64(9),
                    Actualizacion = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
                });
            }
            return lista;
        }

        public async Task<bool> InvalidarSesionPorIdAsync(long idSesion, long idUsuarioAuditoria)
        {
            const string sql = @"
                UPDATE SESIONES_USUARIOS
                SET estado = 0,
                    usuario = @p_usuario,
                    actualizacion = CURRENT_TIMESTAMP
                WHERE id_sesion = @p_id_sesion";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_usuario", idUsuarioAuditoria);
            cmd.Parameters.AddWithValue("p_id_sesion", idSesion);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }

        public async Task<int> InvalidarOtrasSesionesAsync(long idUsuario, long idSesionActual, long auditor)
        {
            const string sql = @"
                UPDATE SESIONES_USUARIOS
                SET estado = 0,
                    usuario = @p_auditor,
                    actualizacion = CURRENT_TIMESTAMP
                WHERE id_usuario = @p_id_usuario
                AND id_sesion <> @p_id_sesion";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_auditor", auditor);
            cmd.Parameters.AddWithValue("p_id_usuario", idUsuario);
            cmd.Parameters.AddWithValue("p_id_sesion", idSesionActual);

            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<int> InvalidarTodasPorUsuarioAsync(long idUsuario)
        {
            const string sql = @"
                UPDATE SESIONES_USUARIOS
                SET estado = 0,
                    actualizacion = CURRENT_TIMESTAMP,
                    usuario = @p_usuario
                WHERE id_usuario = @p_usuario
                AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_usuario", idUsuario);

            return await cmd.ExecuteNonQueryAsync();
        }

        public async Task<SesionUsuario?> ObtenerSesionPorIdAsync(long idSesion)
        {
            const string sql = @"
                SELECT id_sesion, id_usuario, token_refresh, expira_en, 
                    user_agent, ip_origen, estado, creacion, actualizacion
                FROM SESIONES_USUARIOS
                WHERE id_sesion = @p_id_sesion";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_id_sesion", idSesion);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new SesionUsuario
            {
                IdSesion = reader.GetInt64(0),
                IdUsuario = reader.GetInt64(1),
                TokenHash = reader.GetString(2),
                ExpiraEn = reader.GetDateTime(3),
                UserAgent = reader.IsDBNull(4) ? null : reader.GetString(4),
                IpOrigen = reader.IsDBNull(5) ? null : reader.GetString(5),
                Estado = reader.GetInt32(6),
                Creacion = reader.GetDateTime(7),
                Actualizacion = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
            };
        }

        

    }
}
