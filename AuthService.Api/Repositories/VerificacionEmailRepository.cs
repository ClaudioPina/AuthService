using AuthService.Api.Data;
using Npgsql;

namespace AuthService.Api.Repositories
{
    public class VerificacionEmailRepository
    {
        private readonly AppDbContext _db;

        public VerificacionEmailRepository(AppDbContext db)
        {
            _db = db;
        }

        public async Task CrearTokenVerificacionAsync(long idUsuario, string token, DateTime expiraEn)
        {
            const string sql = @"
                INSERT INTO VERIFICACION_EMAIL
                    (id_usuario, token, expira_en, creacion, estado)
                VALUES
                    (@p_id_usuario, @p_token, @p_expira_en, CURRENT_TIMESTAMP, 1)";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_id_usuario", idUsuario);
            cmd.Parameters.AddWithValue("p_token",      token);
            cmd.Parameters.AddWithValue("p_expira_en",  expiraEn);

            await cmd.ExecuteNonQueryAsync();
        }

        // Buscar token
        public async Task<(long IdUsuario, DateTime ExpiraEn)?> ObtenerTokenAsync(string token)
        {
            // Solo tokens activos (estado = 1)
            const string sql = @"
                SELECT id_usuario, expira_en
                FROM VERIFICACION_EMAIL
                WHERE token = @p_token
                AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_token", token);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return (
                reader.GetInt64(0),
                reader.GetDateTime(1)
            );
        }

        // Marcar token como usado
        public async Task InvalidarTokenAsync(string token)
        {
            const string sql = @"
                UPDATE VERIFICACION_EMAIL SET estado = 0
                WHERE token = @p_token";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_token", token);

            await cmd.ExecuteNonQueryAsync();
        }
    }
}
