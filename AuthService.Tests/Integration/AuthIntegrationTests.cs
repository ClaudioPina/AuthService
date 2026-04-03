using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AuthService.Api.Services;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AuthService.Tests.Integration
{
    /// <summary>
    /// Tests de integración: levantan la app completa con PostgreSQL real.
    /// IClassFixture hace que AuthWebAppFactory se cree UNA sola vez para todos
    /// los tests de esta clase (más eficiente que recrearla por test).
    /// PREREQUISITO: Docker debe estar corriendo.
    /// </summary>
    [Collection("AuthIntegration")]
    public class AuthIntegrationTests
    {
        private readonly HttpClient _client;
        private readonly AuthWebAppFactory _factory;

        public AuthIntegrationTests(AuthWebAppFactory factory)
        {
            _factory = factory;
            _client  = factory.CreateClient();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        /// <summary>Registra un usuario de prueba y retorna el link de verificación.</summary>
        private async Task<string> RegisterAndGetVerificationLinkAsync(
            string email    = "test@example.com",
            string password = "TestPass1!")
        {
            var response = await _client.PostAsJsonAsync("/auth/register", new
            {
                email,
                nombre   = "Test User",
                password
            });
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            return body.GetProperty("verificar_url_dev").GetString()!;
        }

        /// <summary>Verifica el email usando el link retornado por el registro.</summary>
        private async Task VerifyEmailAsync(string verificationLink)
        {
            var token    = verificationLink.Split("/").Last();
            var response = await _client.GetAsync($"/auth/verify-email/{token}");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        /// <summary>Registra, verifica y loguea. Retorna los tokens.</summary>
        private async Task<(string accessToken, string refreshToken)> RegisterVerifyAndLoginAsync(
            string email    = "test@example.com",
            string password = "TestPass1!")
        {
            var link = await RegisterAndGetVerificationLinkAsync(email, password);
            await VerifyEmailAsync(link);

            var response = await _client.PostAsJsonAsync("/auth/login", new { email, password });
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body         = await response.Content.ReadFromJsonAsync<JsonElement>();
            var tokens       = body.GetProperty("tokens");
            var accessToken  = tokens.GetProperty("accessToken").GetString()!;
            var refreshToken = tokens.GetProperty("refreshToken").GetString()!;

            return (accessToken, refreshToken);
        }

        /// <summary>
        /// Crea un request HTTP con el header Authorization ya configurado.
        /// Evita mutar DefaultRequestHeaders del cliente compartido, lo que
        /// causaría problemas si los tests se ejecutan en paralelo.
        /// </summary>
        private static HttpRequestMessage CreateAuthRequest(HttpMethod method, string url, string accessToken)
        {
            var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            return request;
        }

        // ── Tests: /auth/register ─────────────────────────────────────────────

        [Fact]
        public async Task Register_WithValidData_Returns201()
        {
            var response = await _client.PostAsJsonAsync("/auth/register", new
            {
                email    = "new_user@example.com",
                nombre   = "New User",
                password = "TestPass1!"
            });

            response.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        [Fact]
        public async Task Register_WithDuplicateEmail_Returns409()
        {
            await RegisterAndGetVerificationLinkAsync("dup@example.com");

            var response = await _client.PostAsJsonAsync("/auth/register", new
            {
                email    = "dup@example.com",
                password = "TestPass1!"
            });

            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }

        [Fact]
        public async Task Register_WithWeakPassword_Returns400()
        {
            var response = await _client.PostAsJsonAsync("/auth/register", new
            {
                email    = "weak@example.com",
                password = "password" // sin mayúscula ni símbolo
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        [Fact]
        public async Task Register_ShouldSendVerificationEmail()
        {
            await RegisterAndGetVerificationLinkAsync("email_check@example.com");

            // Verificar que el FakeEmailService recibió la llamada
            var fake = _factory.Services.GetRequiredService<IEmailService>() as FakeEmailService;
            fake!.VerificationEmails.Should().Contain(e => e.To == "email_check@example.com");
        }

        // ── Tests: /auth/login ────────────────────────────────────────────────

        [Fact]
        public async Task Login_WithValidCredentials_ShouldReturnTokens()
        {
            var link = await RegisterAndGetVerificationLinkAsync("login_ok@example.com");
            await VerifyEmailAsync(link);

            var response = await _client.PostAsJsonAsync("/auth/login", new
            {
                email    = "login_ok@example.com",
                password = "TestPass1!"
            });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("tokens").GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
            body.GetProperty("tokens").GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Login_WithWrongPassword_ShouldReturn400WithGenericMessage()
        {
            var link = await RegisterAndGetVerificationLinkAsync("wrongpass@example.com");
            await VerifyEmailAsync(link);

            var response = await _client.PostAsJsonAsync("/auth/login", new
            {
                email    = "wrongpass@example.com",
                password = "WrongPass1!"
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body    = await response.Content.ReadFromJsonAsync<JsonElement>();
            var message = body.GetProperty("message").GetString();
            // El mensaje debe ser genérico (no revelar si el email existe)
            message.Should().Contain("credenciales proporcionadas");
        }

        [Fact]
        public async Task Login_WithUnverifiedEmail_ShouldReturn400()
        {
            await RegisterAndGetVerificationLinkAsync("unverified@example.com");
            // NO verificamos el email antes de intentar login

            var response = await _client.PostAsJsonAsync("/auth/login", new
            {
                email    = "unverified@example.com",
                password = "TestPass1!"
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("message").GetString().Should().Contain("verificar tu email");
        }

        // ── Tests: /auth/refresh-token ────────────────────────────────────────

        [Fact]
        public async Task RefreshToken_WithValidToken_ShouldReturnNewTokens()
        {
            var (_, refreshToken) = await RegisterVerifyAndLoginAsync("refresh_ok@example.com");

            var response = await _client.PostAsJsonAsync("/auth/refresh-token", new
            {
                refreshToken
            });

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body   = await response.Content.ReadFromJsonAsync<JsonElement>();
            var tokens = body.GetProperty("tokens");
            tokens.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
            tokens.GetProperty("refreshToken").GetString().Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task RefreshToken_WithInvalidToken_ShouldReturn400()
        {
            var response = await _client.PostAsJsonAsync("/auth/refresh-token", new
            {
                refreshToken = "token_que_no_existe"
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // ── Tests: /auth/logout ───────────────────────────────────────────────

        [Fact]
        public async Task Logout_WithValidJwt_ShouldReturn200()
        {
            var (accessToken, _) = await RegisterVerifyAndLoginAsync("logout_ok@example.com");

            var response = await _client.SendAsync(
                CreateAuthRequest(HttpMethod.Post, "/auth/logout", accessToken));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        [Fact]
        public async Task LogoutAll_WithValidJwt_ShouldReturn200()
        {
            var (accessToken, _) = await RegisterVerifyAndLoginAsync("logoutall@example.com");

            var response = await _client.SendAsync(
                CreateAuthRequest(HttpMethod.Post, "/auth/logout-all", accessToken));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // ── Tests: endpoints protegidos ───────────────────────────────────────

        [Fact]
        public async Task Sessions_WithoutJwt_ShouldReturn401()
        {
            _client.DefaultRequestHeaders.Authorization = null;
            var response = await _client.GetAsync("/auth/sessions");
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Fact]
        public async Task Sessions_WithValidJwt_ShouldReturn200WithList()
        {
            var (accessToken, _) = await RegisterVerifyAndLoginAsync("sessions_ok@example.com");

            var response = await _client.SendAsync(
                CreateAuthRequest(HttpMethod.Get, "/auth/sessions", accessToken));

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("sesiones").GetArrayLength().Should().BeGreaterThan(0);
        }

        // ── Tests: health check ───────────────────────────────────────────────

        [Fact]
        public async Task Health_ShouldReturn200()
        {
            var response = await _client.GetAsync("/health");
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // ── Tests: /auth/forgot-password y /auth/reset-password ──────────────────

        [Fact]
        public async Task ForgotPassword_WithRegisteredEmail_ShouldSendResetEmail()
        {
            // Registrar y verificar usuario primero
            var link = await RegisterAndGetVerificationLinkAsync("forgot_ok@example.com");
            await VerifyEmailAsync(link);

            var response = await _client.PostAsJsonAsync("/auth/forgot-password", new
            {
                email = "forgot_ok@example.com"
            });

            // La respuesta siempre es 200 (no revela si el email existe)
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            // Verificar que el FakeEmailService capturó el email de reset
            var fake = _factory.Services.GetRequiredService<IEmailService>() as FakeEmailService;
            fake!.ResetEmails.Should().Contain(e => e.To == "forgot_ok@example.com");
        }

        [Fact]
        public async Task ResetPassword_WithValidToken_ShouldUpdatePassword()
        {
            // Registrar usuario
            var link = await RegisterAndGetVerificationLinkAsync("reset_ok@example.com");
            await VerifyEmailAsync(link);

            // Solicitar reset
            await _client.PostAsJsonAsync("/auth/forgot-password", new
            {
                email = "reset_ok@example.com"
            });

            // Obtener el link de reset del FakeEmailService
            var fake = _factory.Services.GetRequiredService<IEmailService>() as FakeEmailService;
            var resetEntry = fake!.ResetEmails.First(e => e.To == "reset_ok@example.com");
            var resetToken = resetEntry.Link.Split("/").Last();

            // Usar el token para cambiar contraseña
            var response = await _client.PostAsJsonAsync("/auth/reset-password", new
            {
                token       = resetToken,
                newPassword = "NuevoPass1!"
            });

            response.StatusCode.Should().Be(HttpStatusCode.OK);

            // Verificar que el nuevo password funciona
            var loginResponse = await _client.PostAsJsonAsync("/auth/login", new
            {
                email    = "reset_ok@example.com",
                password = "NuevoPass1!"
            });
            loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // ── Tests: account lockout ────────────────────────────────────────────────

        [Fact]
        public async Task Login_AfterMaxFailedAttempts_ShouldReturnLockedError()
        {
            // Registrar y verificar usuario (necesitamos que exista en BD para generar intentos)
            var link = await RegisterAndGetVerificationLinkAsync("lockout_test@example.com");
            await VerifyEmailAsync(link);

            // La factory configura Lockout:MaxIntentos = 3, así que con 3 intentos fallidos
            // el siguiente debe retornar el mensaje de bloqueo.
            for (var i = 0; i < 3; i++)
            {
                await _client.PostAsJsonAsync("/auth/login", new
                {
                    email    = "lockout_test@example.com",
                    password = "WrongPass1!"
                });
            }

            // El 4to intento (después del bloqueo) debe retornar el mensaje de cuenta bloqueada.
            var response = await _client.PostAsJsonAsync("/auth/login", new
            {
                email    = "lockout_test@example.com",
                password = "WrongPass1!"
            });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("message").GetString().Should().Contain("bloqueada");
        }

        // ── Tests: change-password invalida sesiones ──────────────────────────────

        [Fact]
        public async Task ChangePassword_ShouldInvalidateAllActiveSessions()
        {
            // Crear 2 sesiones del mismo usuario (simula 2 dispositivos)
            var link = await RegisterAndGetVerificationLinkAsync("changepwd_sessions@example.com");
            await VerifyEmailAsync(link);

            var (token1, _) = await LoginAsync("changepwd_sessions@example.com");
            var (token2, _) = await LoginAsync("changepwd_sessions@example.com");

            // Cambiar contraseña desde la primera sesión
            using var client1 = _factory.CreateClient();
            client1.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token1);

            var changeResponse = await client1.PostAsJsonAsync("/auth/change-password", new
            {
                passwordActual = "TestPass1!",
                passwordNueva  = "NuevoPass1!"
            });
            changeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // La segunda sesión ya no debe funcionar — ValidarSesionMiddleware la rechaza
            using var client2 = _factory.CreateClient();
            client2.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token2);

            var sessionsResponse = await client2.GetAsync("/auth/sessions");
            sessionsResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // ── Tests: logout-all invalida sesiones ───────────────────────────────────

        [Fact]
        public async Task LogoutAll_ShouldInvalidateAllSessions()
        {
            var link = await RegisterAndGetVerificationLinkAsync("logoutall_sessions@example.com");
            await VerifyEmailAsync(link);

            var (token1, _) = await LoginAsync("logoutall_sessions@example.com");
            var (token2, _) = await LoginAsync("logoutall_sessions@example.com");

            // Hacer logout-all desde el primer token
            using var client1 = _factory.CreateClient();
            client1.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token1);

            var logoutAllResponse = await client1.PostAsync("/auth/logout-all", null);
            logoutAllResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // El segundo token ya no debe ser válido
            using var client2 = _factory.CreateClient();
            client2.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token2);

            var sessionsResponse = await client2.GetAsync("/auth/sessions");
            sessionsResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // ── Tests: revoke-session invalida una sesión específica ─────────────────

        [Fact]
        public async Task RevokeSession_ShouldInvalidateOnlyThatSession()
        {
            var link = await RegisterAndGetVerificationLinkAsync("revoke_session@example.com");
            await VerifyEmailAsync(link);

            var (token1, _) = await LoginAsync("revoke_session@example.com");
            var (token2, _) = await LoginAsync("revoke_session@example.com");

            // Obtener las sesiones activas desde token1
            using var client1 = _factory.CreateClient();
            client1.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token1);

            var sessionsResponse = await client1.GetAsync("/auth/sessions");
            sessionsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var sessionsBody = await sessionsResponse.Content.ReadFromJsonAsync<JsonElement>();
            var sesiones     = sessionsBody.GetProperty("sesiones").EnumerateArray().ToList();

            // Revocar la sesión correspondiente a token2 (la más reciente = primera en la lista)
            var idSesionARevoke = sesiones[0].GetProperty("idSesion").GetInt64();
            var revokeResponse  = await client1.PostAsync($"/auth/sessions/revoke/{idSesionARevoke}", null);
            revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // token2 (la sesión revocada) ya no debe funcionar
            using var client2 = _factory.CreateClient();
            client2.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token2);

            var afterRevokeResponse = await client2.GetAsync("/auth/sessions");
            afterRevokeResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            // token1 (la sesión que hizo la revocación) sigue activo
            var stillActiveResponse = await client1.GetAsync("/auth/sessions");
            stillActiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // ── Tests: refresh token reuse detection ──────────────────────────────────

        [Fact]
        public async Task RefreshToken_ReuseDetected_ShouldRevokeAllSessions()
        {
            var link = await RegisterAndGetVerificationLinkAsync("reuse_detection@example.com");
            await VerifyEmailAsync(link);

            var (accessToken, refreshToken) = await LoginAsync("reuse_detection@example.com");

            // Primer uso del refresh token — exitoso
            var firstRefresh = await _client.PostAsJsonAsync("/auth/refresh-token", new { refreshToken });
            firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);

            // Segundo uso del mismo refresh token ya rotado — debe detectar reutilización
            var secondRefresh = await _client.PostAsJsonAsync("/auth/refresh-token", new { refreshToken });
            secondRefresh.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // El token original de acceso ya no debe funcionar (todas las sesiones revocadas)
            using var clientWithOldToken = _factory.CreateClient();
            clientWithOldToken.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var sessionCheck = await clientWithOldToken.GetAsync("/auth/sessions");
            sessionCheck.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // ── Tests: reset-password invalida sesiones activas ───────────────────────

        [Fact]
        public async Task ResetPassword_ShouldInvalidateAllActiveSessions()
        {
            var link = await RegisterAndGetVerificationLinkAsync("reset_sessions@example.com");
            await VerifyEmailAsync(link);

            // Crear una sesión activa antes de resetear
            var (accessToken, _) = await LoginAsync("reset_sessions@example.com");

            // Solicitar reset
            await _client.PostAsJsonAsync("/auth/forgot-password", new
            {
                email = "reset_sessions@example.com"
            });

            var fake       = _factory.Services.GetRequiredService<IEmailService>() as FakeEmailService;
            var resetEntry = fake!.ResetEmails.First(e => e.To == "reset_sessions@example.com");
            var resetToken = resetEntry.Link.Split("/").Last();

            var resetResponse = await _client.PostAsJsonAsync("/auth/reset-password", new
            {
                token       = resetToken,
                newPassword = "NuevoPass2!"
            });
            resetResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            // La sesión activa anterior ya no debe funcionar
            using var clientWithOldToken = _factory.CreateClient();
            clientWithOldToken.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var sessionCheck = await clientWithOldToken.GetAsync("/auth/sessions");
            sessionCheck.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // ── Helper adicional ──────────────────────────────────────────────────────

        /// <summary>
        /// Hace login y retorna (accessToken, refreshToken).
        /// Asume que el usuario ya está registrado y verificado.
        /// </summary>
        private async Task<(string accessToken, string refreshToken)> LoginAsync(
            string email    = "test@example.com",
            string password = "TestPass1!")
        {
            var response = await _client.PostAsJsonAsync("/auth/login", new { email, password });
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            var body         = await response.Content.ReadFromJsonAsync<JsonElement>();
            var tokens       = body.GetProperty("tokens");
            var accessToken  = tokens.GetProperty("accessToken").GetString()!;
            var refreshToken = tokens.GetProperty("refreshToken").GetString()!;

            return (accessToken, refreshToken);
        }
    }
}
