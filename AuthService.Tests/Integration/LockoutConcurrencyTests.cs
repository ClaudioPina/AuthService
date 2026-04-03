using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Api.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace AuthService.Tests.Integration
{
    /// <summary>
    /// Tests de concurrencia para el UPSERT de INTENTOS_LOGIN.
    /// Verifican que bajo carga simultánea:
    ///   1. El contador de intentos es exacto (no se pierden actualizaciones).
    ///   2. No se crean filas duplicadas para el mismo email.
    ///   3. El lockout se activa correctamente.
    ///
    /// PREREQUISITO: Docker debe estar corriendo.
    /// </summary>
    [Collection("AuthIntegration")]
    public class LockoutConcurrencyTests
    {
        private readonly HttpClient _client;
        private readonly AuthWebAppFactory _factory;

        // MaxIntentos = 3 configurado en AuthWebAppFactory
        private const int MaxIntentos = 3;

        public LockoutConcurrencyTests(AuthWebAppFactory factory)
        {
            _factory = factory;
            _client  = factory.CreateClient();
        }

        [Fact]
        public async Task ConcurrentFailedLogins_ShouldTriggerLockout()
        {
            // Email único para aislar este test de otros tests que corran en paralelo.
            // No necesita ser un usuario real — el intento fallido se registra igualmente.
            var email = $"concurrent-lockout-{Guid.NewGuid()}@test.com";

            // Lanzar exactamente MaxIntentos requests simultáneos con contraseña incorrecta
            var tasks = Enumerable.Range(0, MaxIntentos)
                .Select(_ => _client.PostAsJsonAsync("/auth/login", new
                {
                    email,
                    password = "WrongPass1!"
                }))
                .ToArray();

            await Task.WhenAll(tasks);

            // Tras agotar el límite, el siguiente intento debe retornar mensaje de bloqueo
            var response = await _client.PostAsJsonAsync("/auth/login", new
            {
                email,
                password = "WrongPass1!"
            });

            var body = await response.Content.ReadAsStringAsync();
            body.Should().Contain("bloqueada",
                because: $"después de {MaxIntentos} intentos fallidos concurrentes la cuenta debe quedar bloqueada");
        }

        [Fact]
        public async Task ConcurrentFailedLogins_ShouldNotCreateDuplicateRows()
        {
            var email = $"concurrent-nodup-{Guid.NewGuid()}@test.com";

            // Más requests que MaxIntentos para asegurar que hay colisiones en el UPSERT
            const int numRequests = 10;

            var tasks = Enumerable.Range(0, numRequests)
                .Select(_ => _client.PostAsJsonAsync("/auth/login", new
                {
                    email,
                    password = "WrongPass1!"
                }))
                .ToArray();

            await Task.WhenAll(tasks);

            // Verificar directamente en BD que el UPSERT no creó filas duplicadas.
            // Si hubiera duplicados, el índice único funcional LOWER(email) habría fallado,
            // pero el ON CONFLICT debería haberlo evitado correctamente.
            using var scope  = _factory.Services.CreateScope();
            var db           = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var conn   = await db.GetOpenConnectionAsync();
            using var cmd    = new NpgsqlCommand(
                "SELECT COUNT(*) FROM INTENTOS_LOGIN WHERE LOWER(email) = LOWER(@p_email)", conn);
            cmd.Parameters.AddWithValue("p_email", email);

            var rowCount = Convert.ToInt64(await cmd.ExecuteScalarAsync());

            rowCount.Should().Be(1,
                because: "el UPSERT atómico debe garantizar exactamente 1 fila por email, incluso bajo carga concurrente");
        }

        [Fact]
        public async Task ConcurrentFailedLogins_CounterShouldBeAccurate()
        {
            var email = $"concurrent-count-{Guid.NewGuid()}@test.com";
            // Se usan exactamente MaxIntentos requests para que todos registren su intento
            // ANTES de que el lockout quede activo. Si usáramos más, los requests que pasan
            // el lockout check después del 3er UPSERT no registrarían intento → contador impreciso.
            const int numRequests = MaxIntentos;

            var tasks = Enumerable.Range(0, numRequests)
                .Select(_ => _client.PostAsJsonAsync("/auth/login", new
                {
                    email,
                    password = "WrongPass1!"
                }))
                .ToArray();

            await Task.WhenAll(tasks);

            // El contador en BD debe reflejar exactamente todos los intentos registrados.
            // Si hubiera lost updates por race conditions, el contador sería menor que numRequests.
            using var scope  = _factory.Services.CreateScope();
            var db           = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            using var conn   = await db.GetOpenConnectionAsync();
            using var cmd    = new NpgsqlCommand(
                "SELECT intentos FROM INTENTOS_LOGIN WHERE LOWER(email) = LOWER(@p_email)", conn);
            cmd.Parameters.AddWithValue("p_email", email);

            var intentos = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            intentos.Should().Be(numRequests,
                because: "cada intento fallido concurrente debe incrementar el contador exactamente en 1 (sin lost updates)");
        }
    }
}
