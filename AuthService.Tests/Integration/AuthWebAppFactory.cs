using System.Threading.RateLimiting;
using AuthService.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            await _postgres.StartAsync();
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
                    ["Cors:AllowedOrigins:0"]                    = "http://localhost:5173"
                });
            });

            builder.ConfigureServices(services =>
            {
                // Reemplazar IEmailService con el fake para no enviar emails reales
                var descriptor = services.SingleOrDefault(
                    d => d.ServiceType == typeof(IEmailService));
                if (descriptor != null) services.Remove(descriptor);

                // Singleton para que el mismo fake sea accesible desde el test
                services.AddSingleton<IEmailService, FakeEmailService>();

                // Desactivar rate limiting en tests: PostConfigure corre después de todos
                // los Configure, por lo que sobreescribe las políticas de Program.cs.
                // GetNoLimiter permite requests ilimitados — sin esto los tests obtienen 429.
                services.PostConfigure<RateLimiterOptions>(options =>
                {
                    options.AddPolicy("login-policy",          _ => RateLimitPartition.GetNoLimiter<string>("test"));
                    options.AddPolicy("register-policy",       _ => RateLimitPartition.GetNoLimiter<string>("test"));
                    options.AddPolicy("forgotpassword-policy", _ => RateLimitPartition.GetNoLimiter<string>("test"));
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
