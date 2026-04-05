namespace AuthService.Api.Models
{
    /// <summary>
    /// Entidad de dominio que representa una fila de la tabla <c>USUARIOS</c>.
    /// Se usa para transportar datos entre repositorio y servicio.
    /// </summary>
    public class Usuario
    {
        /// <summary>
        /// Clave primaria del usuario.
        /// </summary>
        public long IdUsuario { get; set; }

        /// <summary>
        /// Email único del usuario (normalizado a lowercase).
        /// </summary>
        public required string Email { get; set; }

        /// <summary>
        /// Nombre visible del usuario. Puede ser null para cuentas creadas vía Google.
        /// </summary>
        public string? Nombre { get; set; }

        /// <summary>
        /// URL de foto de perfil (normalmente provista por Google).
        /// </summary>
        public string? FotoUrl { get; set; }

        /// <summary>
        /// Hash BCrypt de la contraseña.
        /// Es null en cuentas Google-only que no tienen password local.
        /// </summary>
        public string? PasswordHash { get; set; }

        /// <summary>
        /// Método(s) de login permitido(s): LOCAL, GOOGLE o MIXTO.
        /// </summary>
        public string? ProveedorLogin { get; set; }

        /// <summary>
        /// Identificador estable de Google para la cuenta (si está vinculada).
        /// </summary>
        public string? GoogleSub { get; set; }

        /// <summary>
        /// Flag 0/1 que indica si el email fue verificado.
        /// </summary>
        public int EmailVerificado { get; set; }

        /// <summary>
        /// Fecha de creación de la cuenta.
        /// </summary>
        public DateTime Creacion { get; set; }

        /// <summary>
        /// Fecha de última actualización (null si nunca se modificó).
        /// </summary>
        public DateTime? Actualizacion { get; set; }

        /// <summary>
        /// Estado lógico de la cuenta (1 activa, 0 desactivada).
        /// </summary>
        public int Estado { get; set; }
    }
}
