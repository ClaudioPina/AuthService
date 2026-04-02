namespace AuthService.Api.Models
{
    public class Usuario
    {
        public long IdUsuario { get; set; }
        public required string Email { get; set; }  // siempre presente
        public string? Nombre { get; set; }          // opcional en cuentas Google
        public string? FotoUrl { get; set; }          // solo cuentas Google
        public string? PasswordHash { get; set; }    // null en cuentas Google-only
        public string? ProveedorLogin { get; set; }  // "LOCAL", "GOOGLE", "MIXTO"
        public string? GoogleSub { get; set; }       // null en cuentas locales
        public int EmailVerificado { get; set; }
        public int Propietario { get; set; }
        public DateTime Creacion { get; set; }
        public int? UsuarioAud { get; set; }
        public DateTime? Actualizacion { get; set; }
        public int Estado { get; set; }
    }
}
