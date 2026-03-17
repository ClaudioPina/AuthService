using System.Data;
using AuthService.Api.Data;
using Npgsql;
using AuthService.Api.Models;

namespace AuthService.Api.Repositories
{
    public class UsuariosRepository
    {
        private readonly AppDbContext _db;

        public UsuariosRepository(AppDbContext db)
        {
            _db = db;
        }

        // Verifica si un email ya existe en la base de datos
        public async Task<bool> EmailExisteAsync(string email)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM USUARIOS
                WHERE LOWER(email) = LOWER(@p_email)
                AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_email", email);

            var result = await cmd.ExecuteScalarAsync();
            var count = Convert.ToInt32(result);

            return count > 0;
        }

        // Crea un nuevo usuario local y devuelve su ID
        public async Task<long> CrearUsuarioLocalAsync(string email, string? nombre, string passwordHash)
        {
            const string sql = @"
                INSERT INTO USUARIOS
                    (email, nombre, password_hash, proveedor_login,
                    email_verificado, propietario, creacion, estado)
                VALUES
                    (@p_email, @p_nombre, @p_password_hash, 'LOCAL',
                    0, 1, CURRENT_TIMESTAMP, 1)
                RETURNING id_usuario";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_email", email);
            cmd.Parameters.AddWithValue("p_nombre", (object?)nombre ?? DBNull.Value);
            cmd.Parameters.AddWithValue("p_password_hash", passwordHash);

            var result = await cmd.ExecuteScalarAsync();

            return Convert.ToInt64(result);
        }

        // Obtiene un usuario por su email
        public async Task<Usuario?> ObtenerUsuarioPorEmailAsync(string email)
        {
            const string sql = @"
                SELECT id_usuario, email, nombre, password_hash, email_verificado, estado
                FROM USUARIOS
                WHERE LOWER(email) = LOWER(@p_email) AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_email", email);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new Usuario
            {
                IdUsuario = reader.GetInt64(0),
                Email = reader.GetString(1),
                Nombre = reader.IsDBNull(2) ? null : reader.GetString(2),
                PasswordHash = reader.GetString(3),
                EmailVerificado = reader.GetInt32(4),
                Estado = reader.GetInt32(5)
            };
        }

        // Marca el email de un usuario como verificado
        public async Task VerificarEmailAsync(long idUsuario)
        {
            const string sql = @"
                UPDATE USUARIOS
                SET email_verificado = 1
                WHERE id_usuario = @p_id";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_id", idUsuario);

            await cmd.ExecuteNonQueryAsync();
        }

        // Actualiza el password de un usuario
        public async Task ActualizarPasswordAsync(long idUsuario, string newPasswordHash)
        {
            const string sql = @"
                UPDATE USUARIOS
                SET password_hash = @p_pass,
                    actualizacion = CURRENT_TIMESTAMP,
                    usuario = @p_id
                WHERE id_usuario = @p_id";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_pass", newPasswordHash);
            cmd.Parameters.AddWithValue("p_id", idUsuario);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<Usuario?> ObtenerUsuarioPorIdAsync(long idUsuario)
        {
            const string sql = @"
                SELECT id_usuario, email, nombre, password_hash, email_verificado, estado
                FROM USUARIOS
                WHERE id_usuario = @p_id_usuario
                AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();

            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_id_usuario", idUsuario);

            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new Usuario
            {
                IdUsuario = reader.GetInt64(0),
                Email = reader.GetString(1),
                Nombre = reader.IsDBNull(2) ? null : reader.GetString(2),
                PasswordHash = reader.GetString(3),
                EmailVerificado = reader.GetInt32(4),
                Estado = reader.GetInt32(5)
            };
        }


    }
}
