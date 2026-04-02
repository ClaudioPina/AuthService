using AuthService.Api.Data;
using Npgsql;

namespace AuthService.Api.Repositories
{
    /// <summary>
    /// Registra operaciones sensibles en la tabla AUDITORIA para trazabilidad.
    /// Las llamadas deben estar envueltas en try/catch en el caller para que
    /// un fallo de auditoría no interrumpa el flujo principal de la app.
    /// </summary>
    public class AuditoriaRepository
    {
        private readonly AppDbContext _db;

        public AuditoriaRepository(AppDbContext db) => _db = db;

        /// <summary>
        /// Inserta un registro de auditoría.
        /// </summary>
        /// <param name="usuarioId">ID del usuario afectado. Null si no pudo determinarse antes del error.</param>
        /// <param name="accion">Código de acción: LOGIN, CAMBIO_CONTRASENA, RESET_CONTRASENA, REVOCACION_SESION, LOGOUT_ALL.</param>
        /// <param name="ip">IP de origen del request. Null si no está disponible en el contexto del método.</param>
        /// <param name="userAgent">User-Agent del cliente. Null si no está disponible.</param>
        public async Task RegistrarAsync(long? usuarioId, string accion, string? ip, string? userAgent)
        {
            const string sql = @"
                INSERT INTO AUDITORIA (usuario_id, accion, ip, user_agent)
                VALUES (@p_usuario_id, @p_accion, @p_ip, @p_user_agent)";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd  = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_usuario_id", (object?)usuarioId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("p_accion",     accion);
            cmd.Parameters.AddWithValue("p_ip",         (object?)ip        ?? DBNull.Value);
            cmd.Parameters.AddWithValue("p_user_agent", (object?)userAgent  ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
