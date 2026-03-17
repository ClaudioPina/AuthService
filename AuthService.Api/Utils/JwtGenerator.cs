using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using AuthService.Api.Models;

namespace AuthService.Api.Utils
{
    public class JwtGenerator
    {
        private readonly string? _key;
        private readonly string? _issuer;
        private readonly string? _audience;

        public JwtGenerator(IConfiguration config)
        {
            _key = config["Jwt:Key"];
            _issuer = config["Jwt:Issuer"];
            _audience = config["Jwt:Audience"];
        }

        public string GenerateJwt(Usuario usuario, long idSesion)
        {
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_key));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim("id", usuario.IdUsuario.ToString()),
                new Claim("email", usuario.Email),
                new Claim("id_sesion", idSesion.ToString())

            };

            var token = new JwtSecurityToken(
                issuer: _issuer,
                audience: _audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(15),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }

}
