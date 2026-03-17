namespace AuthService.Api.Models
{
    public class ResetPasswordToken
    {
        public long IdReset { get; set; }
        public long IdUsuario { get; set; }
        public string Token { get; set; } = "";
        public DateTime ExpiraEn { get; set; }
        public int Estado { get; set; }
    }
}
