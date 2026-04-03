using System.Security.Cryptography;
using System.Text;

namespace AuthService.Api.Services
{
    /// <summary>
    /// Implementación de IHibpService usando la API pública de Have I Been Pwned.
    ///
    /// Flujo k-anonymity:
    ///   1. Calcular SHA-1 de la contraseña en texto plano.
    ///   2. Enviar solo los primeros 5 caracteres del hash hexadecimal.
    ///   3. La API retorna todos los sufijos que empiezan con ese prefijo.
    ///   4. Buscar si el sufijo restante aparece en la respuesta.
    ///   5. En ningún momento se envía la contraseña completa ni el hash completo.
    /// </summary>
    public class HibpService : IHibpService
    {
        private readonly HttpClient _http;
        private readonly ILogger<HibpService> _logger;

        public HibpService(HttpClient http, ILogger<HibpService> logger)
        {
            _http = http;
            _logger = logger;
        }

        public async Task<bool> EsPasswordCompromisedAsync(string password)
        {
            try
            {
                var hash   = ComputeSha1Hex(password);
                var prefix = hash[..5];   // primeros 5 caracteres
                var suffix = hash[5..];   // resto del hash (lo buscamos en la respuesta)

                var response = await _http.GetStringAsync(
                    $"https://api.pwnedpasswords.com/range/{prefix}");

                // La respuesta es texto plano: "SUFFIX:COUNT\r\n..." — una entrada por línea.
                // Comparamos en uppercase porque SHA-1 hex puede venir en cualquier case.
                return response
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Any(line =>
                    {
                        var parts = line.Split(':');
                        return parts.Length >= 1 &&
                               parts[0].Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase);
                    });
            }
            catch (Exception ex)
            {
                // Fail open: si HIBP no responde (timeout, DNS, etc.), no bloqueamos al usuario.
                // Loguear warning para que sea visible en métricas de error.
                _logger.LogWarning(ex, "HIBP no disponible. Se omite la verificación de contraseña comprometida.");
                return false;
            }
        }

        private static string ComputeSha1Hex(string input)
        {
            var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes); // .NET 5+: retorna uppercase sin guiones
        }
    }
}
