using System.IdentityModel.Tokens.Jwt;
using AuthService.Api.Models;
using AuthService.Api.Utils;
using FluentAssertions;
using Microsoft.Extensions.Configuration;

namespace AuthService.Tests.Unit
{
    public class JwtGeneratorTests
    {
        // Configuración mínima para instanciar JwtGenerator en tests
        private static JwtGenerator CreateGenerator()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Key"]                          = "clave_de_test_de_minimo_32_caracteres_ok!!",
                    ["Jwt:Issuer"]                       = "TestIssuer",
                    ["Jwt:Audience"]                     = "TestAudience",
                    ["Jwt:AccessTokenExpirationMinutes"] = "15"
                })
                .Build();
            return new JwtGenerator(config);
        }

        private static Usuario CreateTestUser() => new()
        {
            IdUsuario = 42,
            Email     = "test@example.com",
            Nombre    = "Test User"
        };

        [Fact]
        public void GenerateJwt_ShouldContainAllRequiredClaims()
        {
            // Un JWT válido debe contener id, email e id_sesion.
            // id_sesion es el que ValidarSesionMiddleware usa para verificar la sesión activa en BD.
            var generator = CreateGenerator();
            var token     = generator.GenerateJwt(CreateTestUser(), idSesion: 99);

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

            jwt.Claims.Should().Contain(c => c.Type == "id"        && c.Value == "42",
                                        "el claim 'id' debe identificar al usuario");
            jwt.Claims.Should().Contain(c => c.Type == "email"     && c.Value == "test@example.com",
                                        "el claim 'email' debe estar presente para conveniencia del cliente");
            jwt.Claims.Should().Contain(c => c.Type == "id_sesion" && c.Value == "99",
                                        "el claim 'id_sesion' es crítico para la validación de sesión activa");
        }

        [Fact]
        public void GenerateJwt_ExpiresInApproximately15Minutes()
        {
            var generator = CreateGenerator();
            var before    = DateTime.UtcNow.AddMinutes(14);
            var after     = DateTime.UtcNow.AddMinutes(16);

            var token = generator.GenerateJwt(CreateTestUser(), idSesion: 1);
            var jwt   = new JwtSecurityTokenHandler().ReadJwtToken(token);

            jwt.ValidTo.Should().BeAfter(before).And.BeBefore(after);
        }

        [Fact]
        public void GenerateJwt_ShouldSetCorrectIssuerAndAudience()
        {
            // El issuer y audience son verificados por el middleware de autenticación
            // en cada request. Si no están presentes, el token es rechazado con 401.
            var generator = CreateGenerator();
            var token     = generator.GenerateJwt(CreateTestUser(), idSesion: 1);

            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);

            jwt.Issuer.Should().Be("TestIssuer");
            jwt.Audiences.Should().Contain("TestAudience");
        }
    }
}
