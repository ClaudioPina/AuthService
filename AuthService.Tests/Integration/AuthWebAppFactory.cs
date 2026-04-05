using System.Threading.RateLimiting;
using AuthService.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;

namespace AuthService.Tests.Integration
{
    /// <summary>
    /// Levanta la aplicación completa en memoria para tests de integración.
    /// Usa Testcontainers para crear una instancia real de PostgreSQL en Docker.
    /// IAsyncLifetime es la interfaz de xUnit para setup/teardown asíncrono.
    /// </summary>
    public class AuthWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        // Testcontainers crea un contenedor Docker con PostgreSQL 16
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();

        /// <summary>
        /// Se ejecuta ANTES de los tests: levanta el contenedor y aplica el schema.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                await _postgres.StartAsync();
            }
            catch (Exception ex)
            {
                // Envolver la excepción críptica de Testcontainers con un mensaje accionable.
                // La causa más común es que Docker Desktop no esté corriendo.
                throw new InvalidOperationException(
                    "No se pudo iniciar el contenedor PostgreSQL para los tests de integración. " +
                    "Asegúrate de que Docker Desktop esté corriendo e inténtalo de nuevo. " +
                    $"Causa original: {ex.Message}", ex);
            }

            await ApplySchemaAsync();
        }

        /// <summary>
        /// Se ejecuta DESPUÉS de los tests: detiene y elimina el contenedor.
        /// </summary>
        public new async Task DisposeAsync()
        {
            await _postgres.StopAsync();
            // Dispose the ASP.NET Core host — frees server, DI scopes, and in-memory services
            await base.DisposeAsync();
        }

        /// <summary>
        /// Sobrescribe la configuración de la app para usar la BD de test
        /// y reemplazar servicios externos (email) con fakes.
        /// </summary>
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                // Inyectar configuración de test (sobreescribe appsettings.json)
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgresDb"]            = _postgres.GetConnectionString(),
                    ["Jwt:Key"]                                  = "clave_de_test_segura_de_al_menos_32_chars!!",
                    ["Jwt:Issuer"]                               = "AuthService",
                    ["Jwt:Audience"]                             = "AuthServiceClients",
                    ["Jwt:AccessTokenExpirationMinutes"]         = "15",
                    ["Tokens:RefreshTokenExpirationDays"]        = "7",
                    ["Tokens:EmailVerificationExpirationHours"]  = "24",
                    ["Tokens:PasswordResetExpirationHours"]      = "1",
                    ["App:BaseUrl"]                              = "https://localhost",
                    ["Email:ResendApiKey"]                       = "test_key",
                    ["Email:FromAddress"]                        = "test@test.com",
                    ["Email:FromName"]                           = "Test",
                    ["Google:ClientId"]                          = "test-google-client-id.apps.googleusercontent.com",
                    ["Cors:AllowedOrigins:0"]                    = "http://localhost:5173",
                    // Lockout reducido para que los tests de bloqueo sean más rápidos
                    ["Lockout:MaxIntentos"]                      = "3",
                    ["Lockout:MinutosBloqueo"]                   = "15"
                });
            });

            builder.ConfigureServices(services =>
            {
                // Reemplazar IEmailService con el fake para no enviar emails reales
                var emailDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IEmailService));
                if (emailDescriptor != null) services.Remove(emailDescriptor);

                // Singleton para que el mismo fake sea accesible desde el test
                services.AddSingleton<IEmailService, FakeEmailService>();

                // Reemplazar IHibpService con fake para evitar llamadas HTTP reales a HIBP.
                // Sin esto, tests con contraseñas comunes fallarían si la API las detecta.
                var hibpDescriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IHibpService));
                if (hibpDescriptor != null) services.Remove(hibpDescriptor);
                services.AddSingleton<IHibpService, FakeHibpService>();

                // Desactivar rate limiting en tests.
                // AddPolicy lanza ArgumentException si el nombre ya existe, por eso no
                // alcanza con PostConfigure — primero hay que eliminar el IConfigureOptions
                // que registró Program.cs y luego agregar políticas sin límite.
                var rateLimiterConfig = services
                    .Where(d => d.ServiceType == typeof(IConfigureOptions<RateLimiterOptions>))
                    .ToList();
                foreach (var d in rateLimiterConfig) services.Remove(d);

                services.Configure<RateLimiterOptions>(options =>
                {
                    options.AddPolicy("login-policy",                _ => RateLimitPartition.GetNoLimiter<string>("test"));
                    options.AddPolicy("register-policy",             _ => RateLimitPartition.GetNoLimiter<string>("test"));
                    options.AddPolicy("forgotpassword-policy",       _ => RateLimitPartition.GetNoLimiter<string>("test"));
                    options.AddPolicy("resendverification-policy",   _ => RateLimitPartition.GetNoLimiter<string>("test"));
                });
            });
        }

        /// <summary>
        /// Ejecuta script_DB.sql en la BD de test para crear las tablas.
        /// El archivo se copia al output del test (configurado en el .csproj).
        /// </summary>
        private async Task ApplySchemaAsync()
        {
            var scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "script_DB.sql");
            var sql        = await File.ReadAllTextAsync(scriptPath);

            await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
            await conn.OpenAsync();

            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
