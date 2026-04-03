using AuthService.Api.Dtos.Auth;

namespace AuthService.Api.Services
{
    /// <summary>
    /// Contrato del servicio de autenticación. Define todas las operaciones
    /// disponibles. Usar una interfaz permite reemplazar la implementación
    /// real por un fake o mock en los tests de integración.
    /// </summary>
    public interface IAutenticacionService
    {
        Task<IResult> RegisterAsync(RegisterRequest request);
        Task<IResult> LoginAsync(LoginRequest request, string? userAgent, string? ip);
        Task<IResult> VerifyEmailAsync(string token);
        Task<IResult> ForgotPasswordAsync(ForgotPasswordRequest request);
        Task<IResult> ResetPasswordAsync(ResetPasswordRequest request);
        Task<IResult> RefreshTokenAsync(RefreshTokenRequest request, string? userAgent, string? ip);
        Task<IResult> ChangePasswordAsync(ChangePasswordRequest request, long idUsuario);
        Task<IResult> LogoutAsync(long idSesion);
        Task<IResult> LogoutAllAsync(long idUsuario);
        Task<IResult> GetSessionsAsync(long idUsuario);
        Task<IResult> RevokeSessionAsync(long idSesion, long idUsuario);
        Task<IResult> GoogleLoginAsync(string idToken, string? userAgent, string? ip);
        Task<IResult> ObtenerPerfilAsync(long idUsuario);
        Task<IResult> ResendVerificationAsync(ResendVerificationRequest request);
    }
}
