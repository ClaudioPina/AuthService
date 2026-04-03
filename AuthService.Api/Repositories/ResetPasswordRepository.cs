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

        // Crear token de recuperación o confirmación de cambio de contraseña.
        // tipo: "reset" (forgot-password) | "change_confirm" (change-password con email)
        // nuevoPasswordHash: solo requerido cuando tipo = "change_confirm"
        public async Task<long> CrearTokenResetAsync(
            long idUsuario,
            string token,
            DateTime expiraEn,
            string tipo = "reset",
            string? nuevoPasswordHash = null)
        {
            const string sql = @"
                INSERT INTO RESET_PASSWORD
                    (id_usuario, token, expira_en, estado, tipo, nuevo_password_hash)
                VALUES
                    (@p_id, @p_token, @p_expira_en, 1, @p_tipo, @p_hash)
                RETURNING id_reset";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd  = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_id",        idUsuario);
            cmd.Parameters.AddWithValue("p_token",     token);
            cmd.Parameters.AddWithValue("p_expira_en", expiraEn);
            cmd.Parameters.AddWithValue("p_tipo",      tipo);
            cmd.Parameters.AddWithValue("p_hash",      (object?)nuevoPasswordHash ?? DBNull.Value);

            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }


        // Obtener un token válido (no usado, no expirado) filtrado por tipo.
        public async Task<ResetPasswordToken?> ObtenerTokenValidoAsync(string token, string tipo = "reset")
        {
            const string sql = @"
                SELECT id_reset, id_usuario, token, expira_en, estado, tipo, nuevo_password_hash
                FROM RESET_PASSWORD
                WHERE token     = @p_token
                  AND tipo      = @p_tipo
                  AND estado    = 1
                  AND expira_en > CURRENT_TIMESTAMP";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd  = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_token", token);
            cmd.Parameters.AddWithValue("p_tipo",  tipo);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new ResetPasswordToken
            {
                IdReset           = reader.GetInt64(0),
                IdUsuario         = reader.GetInt64(1),
                Token             = reader.GetString(2),
                ExpiraEn          = reader.GetDateTime(3),
                Estado            = reader.GetInt32(4),
                Tipo              = reader.GetString(5),
                NuevoPasswordHash = reader.IsDBNull(6) ? null : reader.GetString(6)
            };
        }


        // Marcar token como usado
        public async Task<bool> MarcarTokenComoUsadoAsync(long idReset)
        {
            const string sql = @"
                UPDATE RESET_PASSWORD SET estado = 0
                WHERE id_reset = @p_id AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_id", idReset);

            return await cmd.ExecuteNonQueryAsync() > 0;
        }


        /// <summary>
        /// Overload transaccional de MarcarTokenComoUsadoAsync.
        /// Usa la conexión y transacción compartidas provistas por el caller.
        /// </summary>
        public async Task<bool> MarcarTokenComoUsadoAsync(long idReset, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            const string sql = @"
                UPDATE RESET_PASSWORD SET estado = 0
                WHERE id_reset = @p_id AND estado = 1";

            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("p_id", idReset);
            return await cmd.ExecuteNonQueryAsync() > 0;
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
