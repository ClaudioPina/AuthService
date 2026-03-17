using System.Data;
using Npgsql;
using AuthService.Api.Data;
using AuthService.Api.Models;

namespace AuthService.Api.Repositories
{
    public class ResetPasswordRepository
    {
        private readonly AppDbContext _db;

        public ResetPasswordRepository(AppDbContext db)
        {
            _db = db;
        }

        // Crear token de recuperación
        public async Task<long> CrearTokenResetAsync(
            long idUsuario,
            string token,
            DateTime expiraEn)
        {
            const string sql = @"
                INSERT INTO RESET_PASSWORD
                    (id_usuario, token, expira_en,
                    propietario, estado)
                VALUES
                    (@p_id_usuario, @p_token,
                    @p_expira_en, @p_id_usuario, 1)
                RETURNING id_reset";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_id_usuario", idUsuario);
            cmd.Parameters.AddWithValue("p_token", token);
            cmd.Parameters.AddWithValue("p_expira_en", expiraEn);

            var result = await cmd.ExecuteScalarAsync();

            return Convert.ToInt64(result);
        }


        // Obtener un token válido (no usado, no expirado)
        public async Task<ResetPasswordToken?> ObtenerTokenValidoAsync(string token)
        {
            const string sql = @"
                SELECT id_reset, id_usuario, token, expira_en, estado
                FROM RESET_PASSWORD
                WHERE token = @p_token
                AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_token", token);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new ResetPasswordToken
            {
                IdReset = reader.GetInt64(0),
                IdUsuario = reader.GetInt64(1),
                Token = reader.GetString(2),
                ExpiraEn = reader.GetDateTime(3),
                Estado = reader.GetInt32(4)
            };
        }


        // Marcar token como usado
        public async Task<bool> MarcarTokenComoUsadoAsync(long idReset, long idUsuarioAccion)
        {
            const string sql = @"
                UPDATE RESET_PASSWORD
                SET estado = 0,
                    usuario = @p_usuario,
                    actualizacion = CURRENT_TIMESTAMP
                WHERE id_reset = @p_id_reset
                AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_usuario", idUsuarioAccion);
            cmd.Parameters.AddWithValue("p_id_reset", idReset);

            var rows = await cmd.ExecuteNonQueryAsync();
            return rows > 0;
        }


        // Desactivar tokens vencidos
        public async Task<int> InvalidarTokensExpiradosAsync()
        {
            const string sql = @"
                UPDATE RESET_PASSWORD
                SET estado = 0
                WHERE expira_en < CURRENT_TIMESTAMP
                AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);

            return await cmd.ExecuteNonQueryAsync();
        }

    }
}
