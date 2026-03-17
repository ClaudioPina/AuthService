namespace AuthService.Api.Models
{
    public class Usuario
    {
        public long IdUsuario { get; set; }
        public string Email { get; set; }
        public string Nombre { get; set; }
        public string FotoUrl { get; set; }
        public string PasswordHash { get; set; }
        public string ProveedorLogin { get; set; }
        public string GoogleSub { get; set; }
        public int EmailVerificado { get; set; }
        public int Propietario { get; set; }
        public DateTime Creacion { get; set; }
        public int? UsuarioAud { get; set; }
        public DateTime? Actualizacion { get; set; }
        public int Estado { get; set; }
    }
}
