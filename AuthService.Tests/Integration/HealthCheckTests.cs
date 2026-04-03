using System.Net;
using System.Text.Json;
using AuthService.Api.Data;
using AuthService.Api.HealthChecks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AuthService.Tests.Integration
{
    /// <summary>
    /// Tests para el endpoint GET /health y para los health checks individuales.
    /// Los tests de HTTP usan la factory compartida (PostgreSQL real en Docker).
    /// El test de "dependencia caída" crea AppDbContext directamente con conexión inválida
    /// para evitar el problema de "Serilog already frozen" al crear una segunda factory.
    /// </summary>
    [Collection("AuthIntegration")]
    public class HealthCheckTests
    {
        private readonly HttpClient _client;

        public HealthCheckTests(AuthWebAppFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task HealthCheck_WithPostgresAvailable_ShouldReturnHealthy()
        {
            var response = await _client.GetAsync("/health");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body   = await response.Content.ReadAsStringAsync();
            var json   = JsonDocument.Parse(body).RootElement;
            json.GetProperty("status").GetString().Should().Be("Healthy");

            var postgresCheck = json.GetProperty("checks")
                .EnumerateArray()
                .FirstOrDefault(c => c.GetProperty("name").GetString() == "postgres");

            postgresCheck.ValueKind.Should().NotBe(JsonValueKind.Undefined,
                because: "debe existir un check llamado 'postgres'");
            postgresCheck.GetProperty("status").GetString().Should().Be("Healthy");
        }

        [Fact]
        public async Task HealthCheck_WithRedisNotConfigured_ShouldReturnHealthyWithNote()
        {
            var response = await _client.GetAsync("/health");

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body       = await response.Content.ReadAsStringAsync();
            var json       = JsonDocument.Parse(body).RootElement;
            var redisCheck = json.GetProperty("checks")
                .EnumerateArray()
                .FirstOrDefault(c => c.GetProperty("name").GetString() == "redis");

            redisCheck.ValueKind.Should().NotBe(JsonValueKind.Undefined,
                because: "debe existir un check llamado 'redis'");
            redisCheck.GetProperty("status").GetString().Should().Be("Healthy");
            redisCheck.GetProperty("description").GetString()
                .Should().Contain("Redis no configurado",
                    because: "sin Redis:ConnectionString el check debe indicar que usa MemoryCache");
        }

        [Fact]
        public async Task PostgresHealthCheck_WhenDatabaseUnavailable_ShouldReturnUnhealthy()
        {
            // Test directo del health check sin levantar una segunda factory.
            // Crea AppDbContext con una conexión inválida (puerto 9999 no tiene PostgreSQL).
            // Timeout=1 para que la conexión falle rápido; Polly reintentará 3 veces (~1.8s total).
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgresDb"] =
                        "Host=127.0.0.1;Port=9999;Database=noexiste;Username=test;Password=test;Timeout=1;Command Timeout=1"
                })
                .Build();

            var dbContext = new AppDbContext(config);
            var check     = new PostgresHealthCheck(dbContext);
            var context   = new HealthCheckContext
            {
                Registration = new HealthCheckRegistration(
                    "postgres", check, HealthStatus.Unhealthy, null)
            };

            var result = await check.CheckHealthAsync(context);

            result.Status.Should().Be(HealthStatus.Unhealthy,
                because: "con una conexión PostgreSQL inválida el check debe reportar Unhealthy");
        }
    }
}
