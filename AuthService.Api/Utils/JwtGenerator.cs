using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthService.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace AuthService.Api.Utils
{
    /// <summary>
    /// Genera JWT Access Tokens firmados con HMAC-SHA256.
    /// Configuración requerida: Jwt:Key, Jwt:Issuer, Jwt:Audience,
    /// Jwt:AccessTokenExpirationMinutes (opcional, default 15).
    /// </summary>
    public class JwtGenerator
    {
        private readonly string _key;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _expirationMinutes;

        public JwtGenerator(IConfiguration config)
        {
            _key               = config["Jwt:Key"]!;
            _issuer            = config["Jwt:Issuer"]!;
            _audience          = config["Jwt:Audience"]!;
            // Si no está configurado, usa 15 minutos como valor por defecto
            _expirationMinutes = config.GetValue<int>("Jwt:AccessTokenExpirationMinutes", 15);
        }

        /// <summary>
        /// Genera un JWT con los claims del usuario y el ID de sesión.
        /// El ID de sesión es el que ValidarSesionMiddleware verifica en cada request.
        /// </summary>
        public string GenerateJwt(Usuario usuario, long idSesion)
        {
            var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim("id",        usuario.IdUsuario.ToString()),
                new Claim("email",     usuario.Email),
                new Claim("id_sesion", idSesion.ToString())
            };

            var token = new JwtSecurityToken(
                issuer:             _issuer,
                audience:           _audience,
                claims:             claims,
                expires:            DateTime.UtcNow.AddMinutes(_expirationMinutes),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>Expone la expiración configurada para incluirla en la respuesta del login.</summary>
        public int ExpirationMinutes => _expirationMinutes;
    }
}
