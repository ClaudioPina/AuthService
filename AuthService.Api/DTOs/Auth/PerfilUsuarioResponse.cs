namespace AuthService.Api.Dtos.Auth
{
    /// <summary>
    /// Datos públicos del usuario autenticado devueltos por GET /auth/me.
    /// No incluye password_hash ni google_sub.
    /// </summary>
    public class PerfilUsuarioResponse
    {
        public long Id { get; set; }
        public string Email { get; set; } = string.Empty;
        public string? Nombre { get; set; }
        public string? FotoUrl { get; set; }
        public bool EmailVerificado { get; set; }
        public string? ProveedorLogin { get; set; }
        public DateTime Creacion { get; set; }
    }
}
