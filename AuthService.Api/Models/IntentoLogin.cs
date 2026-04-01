namespace AuthService.Api.Models
{
    public class IntentoLogin
    {
        public long IdIntento { get; set; }
        public string Email { get; set; } = null!;
        public string IpOrigen { get; set; } = null!;
        public int Intentos { get; set; }
        public DateTime UltimoIntento { get; set; }
        public DateTime? BloqueadoHasta { get; set; }
    }
}
