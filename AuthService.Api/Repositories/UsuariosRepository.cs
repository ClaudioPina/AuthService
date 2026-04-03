using AuthService.Api.Data;
using AuthService.Api.Models;
using Npgsql;

namespace AuthService.Api.Repositories
{
    /// <summary>
    /// Repositorio de usuarios: encapsula todas las queries SQL relacionadas
    /// con la tabla USUARIOS. No contiene lógica de negocio.
    /// </summary>
    public class UsuariosRepository
    {
        private readonly AppDbContext _db;

        public UsuariosRepository(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Verifica si ya existe un usuario registrado con ese email.
        /// La comparación es case-insensitive (LOWER) para evitar duplicados como
        /// "User@mail.com" y "user@mail.com".
        /// </summary>
        public async Task<bool> EmailExisteAsync(string email)
        {
            const string sql = @"
                SELECT COUNT(1) FROM USUARIOS
                WHERE LOWER(email) = LOWER(@p_email)";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_email", email);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt32(result) > 0;
        }

        /// <summary>
        /// Crea un nuevo usuario con login local (email + password).
        /// Retorna el ID generado por PostgreSQL (RETURNING id_usuario).
        /// </summary>
        public async Task<long> CrearUsuarioLocalAsync(string email, string? nombre, string passwordHash)
        {
            const string sql = @"
                INSERT INTO USUARIOS
                    (email, nombre, password_hash, proveedor_login, email_verificado, creacion, estado)
                VALUES
                    (@p_email, @p_nombre, @p_password_hash, 'LOCAL', 0, CURRENT_TIMESTAMP, 1)
                RETURNING id_usuario";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("p_email", email);
            // DBNull.Value es la forma de pasar NULL en Npgsql para parámetros opcionales
            cmd.Parameters.AddWithValue("p_nombre", (object?)nombre ?? DBNull.Value);
            cmd.Parameters.AddWithValue("p_password_hash", passwordHash);

            var result = await cmd.ExecuteScalarAsync();
            return Convert.ToInt64(result);
        }

        /// <summary>
        /// Busca un usuario activo por email. Retorna null si no existe o está desactivado.
        /// </summary>
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
            if (!await reader.ReadAsync()) return null;

            return new Usuario
            {
                IdUsuario       = reader.GetInt64(0),
                Email           = reader.GetString(1),
                Nombre          = reader.IsDBNull(2) ? null : reader.GetString(2),
                PasswordHash    = reader.IsDBNull(3) ? null : reader.GetString(3),
                EmailVerificado = reader.GetInt32(4),
                Estado          = reader.GetInt32(5)
            };
        }

        /// <summary>
        /// Busca un usuario activo por su ID numérico.
        /// Retorna todos los campos relevantes, incluyendo foto_url, proveedor_login y creacion.
        /// </summary>
        public async Task<Usuario?> ObtenerUsuarioPorIdAsync(long idUsuario)
        {
            const string sql = @"
                SELECT id_usuario, email, nombre, password_hash, email_verificado, estado,
                       foto_url, proveedor_login, creacion
                FROM USUARIOS
                WHERE id_usuario = @p_id AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_id", idUsuario);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new Usuario
            {
                IdUsuario       = reader.GetInt64(0),
                Email           = reader.GetString(1),
                Nombre          = reader.IsDBNull(2) ? null : reader.GetString(2),
                PasswordHash    = reader.IsDBNull(3) ? null : reader.GetString(3),
                EmailVerificado = reader.GetInt32(4),
                Estado          = reader.GetInt32(5),
                FotoUrl         = reader.IsDBNull(6) ? null : reader.GetString(6),
                ProveedorLogin  = reader.IsDBNull(7) ? null : reader.GetString(7),
                Creacion        = reader.GetDateTime(8)
            };
        }

        /// <summary>
        /// Marca el email de un usuario como verificado (email_verificado = 1).
        /// </summary>
        public async Task VerificarEmailAsync(long idUsuario)
        {
            const string sql = @"
                UPDATE USUARIOS
                SET email_verificado = 1,
                    actualizacion     = NOW()
                WHERE id_usuario = @p_id";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_id", idUsuario);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Busca un usuario activo por su google_sub (identificador único de Google).
        /// Se usa en el flujo de login con Google para encontrar usuarios existentes.
        /// </summary>
        public async Task<Usuario?> ObtenerUsuarioPorGoogleSubAsync(string googleSub)
        {
            const string sql = @"
                SELECT id_usuario, email, nombre, password_hash, email_verificado, estado
                FROM USUARIOS
                WHERE google_sub = @p_sub AND estado = 1";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_sub", googleSub);

            using var reader = await cmd.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return null;

            return new Usuario
            {
                IdUsuario       = reader.GetInt64(0),
                Email           = reader.GetString(1),
                Nombre          = reader.IsDBNull(2) ? null : reader.GetString(2),
                PasswordHash    = reader.IsDBNull(3) ? null : reader.GetString(3),
                EmailVerificado = reader.GetInt32(4),
                Estado          = reader.GetInt32(5)
            };
        }

        /// <summary>
        /// Crea un nuevo usuario cuya cuenta fue creada exclusivamente vía Google.
        /// El email ya viene verificado por Google (email_verificado = 1).
        /// password_hash es NULL porque no usa contraseña local.
        /// </summary>
        public async Task<long> CrearUsuarioGoogleAsync(string email, string? nombre, string? fotoUrl, string googleSub)
        {
            const string sql = @"
                INSERT INTO USUARIOS
                    (email, nombre, foto_url, password_hash, proveedor_login, google_sub, email_verificado, creacion, estado)
                VALUES
                    (@p_email, @p_nombre, @p_foto, NULL, 'GOOGLE', @p_sub, 1, CURRENT_TIMESTAMP, 1)
                RETURNING id_usuario";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_email",  email);
            cmd.Parameters.AddWithValue("p_nombre", (object?)nombre  ?? DBNull.Value);
            cmd.Parameters.AddWithValue("p_foto",   (object?)fotoUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("p_sub",    googleSub);

            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        /// <summary>
        /// Vincula un google_sub a una cuenta existente (login local + Google).
        /// Cambia proveedor_login a 'MIXTO' para indicar que tiene ambos métodos.
        /// Se llama cuando un usuario con cuenta local inicia sesión con Google
        /// usando el mismo email.
        /// </summary>
        public async Task VincularGoogleSubAsync(long idUsuario, string googleSub, string? fotoUrl)
        {
            const string sql = @"
                UPDATE USUARIOS
                SET google_sub       = @p_sub,
                    foto_url         = COALESCE(@p_foto, foto_url),
                    proveedor_login  = 'MIXTO',
                    email_verificado = 1,
                    actualizacion    = NOW()
                WHERE id_usuario = @p_id";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_id",   idUsuario);
            cmd.Parameters.AddWithValue("p_sub",  googleSub);
            cmd.Parameters.AddWithValue("p_foto", (object?)fotoUrl ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Actualiza el hash de la contraseña.
        /// </summary>
        public async Task ActualizarPasswordAsync(long idUsuario, string newPasswordHash)
        {
            const string sql = @"
                UPDATE USUARIOS
                SET password_hash = @p_pass,
                    actualizacion = NOW()
                WHERE id_usuario = @p_id";

            using var conn = await _db.GetOpenConnectionAsync();
            using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("p_pass", newPasswordHash);
            cmd.Parameters.AddWithValue("p_id", idUsuario);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Overload transaccional de ActualizarPasswordAsync.
        /// Usa la conexión y transacción compartidas provistas por el caller.
        /// </summary>
        public async Task ActualizarPasswordAsync(long idUsuario, string newPasswordHash, NpgsqlConnection conn, NpgsqlTransaction tx)
        {
            const string sql = @"
                UPDATE USUARIOS
                SET password_hash = @p_pass,
                    actualizacion = NOW()
                WHERE id_usuario = @p_id";

            using var cmd = new NpgsqlCommand(sql, conn, tx);
            cmd.Parameters.AddWithValue("p_pass", newPasswordHash);
            cmd.Parameters.AddWithValue("p_id", idUsuario);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
