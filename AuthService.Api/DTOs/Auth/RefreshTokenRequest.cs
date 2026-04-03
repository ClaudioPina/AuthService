namespace AuthService.Api.Dtos.Auth
{
    /// <summary>
    /// Payload de entrada para <c>POST /auth/refresh-token</c>.
    /// </summary>
    public class RefreshTokenRequest
    {
        /// <summary>
        /// Refresh token en texto plano entregado al cliente en el login/refresh anterior.
        /// El backend lo hashea y busca su sesión asociada.
        /// </summary>
        public string RefreshToken { get; set; } = null!;
    }
}
