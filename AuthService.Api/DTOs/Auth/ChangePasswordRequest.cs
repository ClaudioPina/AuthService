namespace AuthService.Api.Dtos.Auth
{
    public class ChangePasswordRequest
    {
        public string PasswordActual { get; set; } = null!;
        public string PasswordNueva { get; set; } = null!;
    }
}
