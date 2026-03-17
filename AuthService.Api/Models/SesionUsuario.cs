namespace AuthService.Api.Models
{
    public class SesionUsuario
    {
        public long IdSesion { get; set; }
        public long IdUsuario { get; set; }
        public string TokenHash { get; set; } = null!;

        public DateTime ExpiraEn { get; set; }
        public int Estado { get; set; }

        public string? UserAgent { get; set; }
        public string? IpOrigen { get; set; }

        public long Propietario { get; set; }
        public DateTime Creacion { get; set; }
        
        public long? UsuarioAuditoria { get; set; }
        public DateTime? Actualizacion { get; set; }
    }
}
