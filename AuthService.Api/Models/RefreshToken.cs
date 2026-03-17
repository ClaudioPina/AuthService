namespace AuthService.Api.Models
{
    public class RefreshToken
    {
        public long IdRefresh { get; set; }
        public long IdUsuario { get; set; }
        public string Token { get; set; } = null!;
        public DateTime ExpiraEn { get; set; }
        public int Estado { get; set; }
    }
}
