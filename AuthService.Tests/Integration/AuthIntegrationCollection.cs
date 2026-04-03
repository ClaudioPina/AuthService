namespace AuthService.Tests.Integration
{
    /// <summary>
    /// Define una colección xUnit para que AuthWebAppFactory se cree UNA sola vez
    /// y se comparta entre todas las clases de test de integración.
    /// Sin esto, cada IClassFixture crea su propia instancia y Serilog falla con
    /// "The logger is already frozen" al intentar inicializar el host por segunda vez.
    /// </summary>
    [CollectionDefinition("AuthIntegration")]
    public class AuthIntegrationCollection : ICollectionFixture<AuthWebAppFactory>
    {
        // Esta clase no contiene código — solo sirve como marcador para xUnit.
    }
}
