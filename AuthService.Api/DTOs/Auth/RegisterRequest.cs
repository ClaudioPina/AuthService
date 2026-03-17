namespace AuthService.Api.Dtos.Auth
{
    public class RegisterRequest
    {
        public string Email { get; set; } = string.Empty;
        public string? Nombre { get; set; }
        public string Password { get; set; } = string.Empty;
    }
}
