using System.Diagnostics.Metrics;

namespace AuthService.Api.Services
{
    /// <summary>
    /// Contadores de métricas de negocio expuestos en /metrics (formato Prometheus).
    ///
    /// Usa System.Diagnostics.Metrics (API nativa de .NET 8) que OpenTelemetry lee
    /// a través del MeterProvider registrado en Program.cs.
    ///
    /// Nomenclatura: snake_case con sufijo _total (convención Prometheus).
    /// Cada counter tiene un tag "result" para segmentar por resultado sin duplicar counters.
    /// </summary>
    public class AuthMetrics
    {
        private readonly Counter<long> _registrations;
        private readonly Counter<long> _logins;
        private readonly Counter<long> _tokenRefreshes;

        public AuthMetrics(IMeterFactory meterFactory)
        {
            // El nombre del Meter ("AuthService") debe coincidir con AddMeter() en Program.cs
            var meter = meterFactory.Create("AuthService");

            _registrations  = meter.CreateCounter<long>(
                "auth_registrations_total",
                description: "Total de intentos de registro.");

            _logins         = meter.CreateCounter<long>(
                "auth_logins_total",
                description: "Total de intentos de login.");

            _tokenRefreshes = meter.CreateCounter<long>(
                "auth_token_refreshes_total",
                description: "Total de rotaciones de refresh token.");
        }

        /// <summary>
        /// Registra un intento de registro.
        /// </summary>
        /// <param name="result">
        ///   "success"          — usuario creado correctamente<br/>
        ///   "validation_error" — email/password inválido o política no cumplida<br/>
        ///   "conflict"         — email ya registrado
        /// </param>
        public void RecordRegistration(string result) =>
            _registrations.Add(1, new KeyValuePair<string, object?>("result", result));

        /// <summary>
        /// Registra un intento de login.
        /// </summary>
        /// <param name="result">
        ///   "success"              — login correcto<br/>
        ///   "invalid_credentials"  — email no existe o contraseña incorrecta<br/>
        ///   "email_not_verified"   — cuenta sin verificar<br/>
        ///   "blocked"              — cuenta bloqueada por intentos fallidos
        /// </param>
        public void RecordLogin(string result) =>
            _logins.Add(1, new KeyValuePair<string, object?>("result", result));

        /// <summary>
        /// Registra una operación de refresh token.
        /// </summary>
        /// <param name="result">
        ///   "success"        — rotación exitosa<br/>
        ///   "expired"        — token expirado<br/>
        ///   "invalid"        — token inválido o usuario inactivo<br/>
        ///   "reuse_detected" — token ya usado (posible robo)
        /// </param>
        public void RecordTokenRefresh(string result) =>
            _tokenRefreshes.Add(1, new KeyValuePair<string, object?>("result", result));
    }
}
