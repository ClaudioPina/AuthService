namespace AuthService.Api.Services
{
    /// <summary>
    /// Verifica si una contraseña aparece en filtraciones conocidas
    /// usando la API pública de Have I Been Pwned con k-anonymity.
    /// La contraseña nunca se envía completa — solo los primeros 5 caracteres del hash SHA-1.
    /// </summary>
    public interface IHibpService
    {
        /// <summary>
        /// Retorna true si la contraseña aparece en al menos una filtración conocida.
        /// Retorna false si está limpia O si la API no está disponible (fail open).
        /// </summary>
        Task<bool> EsPasswordCompromisedAsync(string password);
    }
}
