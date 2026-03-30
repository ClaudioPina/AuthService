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
    public class AuthIntegrationTests : IClassFixture<AuthWebAppFactory>
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
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
            body.GetProperty("refresh_token").GetString().Should().NotBeNullOrEmpty();
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
    }
}
