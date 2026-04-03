using AuthService.Api.Services;

namespace AuthService.Tests.Integration
{
    /// <summary>
    /// Implementación fake de IHibpService para tests de integración.
    /// Siempre retorna false (no comprometida) para no hacer llamadas HTTP reales
    /// y evitar tests flaky dependientes de disponibilidad de la API de HIBP.
    /// </summary>
    public class FakeHibpService : IHibpService
    {
        public Task<bool> EsPasswordCompromisedAsync(string password) => Task.FromResult(false);
    }
}
