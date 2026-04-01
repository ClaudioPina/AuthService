using AuthService.Api.Dtos.Auth;
using AuthService.Api.Repositories;
using AuthService.Api.Utils;

namespace AuthService.Api.Services
{
    /// <summary>
    /// Contiene toda la lógica de negocio del microservicio de autenticación.
    /// Orquesta los repositorios y el servicio de email para implementar
    /// cada operación (registro, login, logout, etc.).
    ///
    /// Retorna IResult directamente para que AuthEndpoints solo mapee la ruta
    /// y delegue la lógica aquí.
    /// </summary>
    public class AutenticacionService : IAutenticacionService
    {
        private readonly UsuariosRepository _usuariosRepo;
        private readonly SesionesUsuariosRepository _sesionesRepo;
        private readonly VerificacionEmailRepository _verifRepo;
        private readonly ResetPasswordRepository _resetRepo;
        private readonly IntentosLoginRepository _intentosRepo;
        private readonly JwtGenerator _jwtGenerator;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<AutenticacionService> _logger;

        public AutenticacionService(
            UsuariosRepository usuariosRepo,
            SesionesUsuariosRepository sesionesRepo,
            VerificacionEmailRepository verifRepo,
            ResetPasswordRepository resetRepo,
            IntentosLoginRepository intentosRepo,
            JwtGenerator jwtGenerator,
            IEmailService emailService,
            IConfiguration config,
            IWebHostEnvironment env,
            ILogger<AutenticacionService> logger)
        {
            _usuariosRepo  = usuariosRepo;
            _sesionesRepo  = sesionesRepo;
            _verifRepo     = verifRepo;
            _resetRepo     = resetRepo;
            _intentosRepo  = intentosRepo;
            _jwtGenerator  = jwtGenerator;
            _emailService  = emailService;
            _config        = config;
            _env           = env;
            _logger        = logger;
        }

        // ── Registro ─────────────────────────────────────────────────────────────

        public async Task<IResult> RegisterAsync(RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { message = "El email y la contraseña son obligatorios." });
            }

            if (!EsEmailValido(request.Email))
                return Results.BadRequest(new { message = "El formato del email no es válido." });

            var (isValid, error) = PasswordPolicy.Validate(request.Password);
            if (!isValid)
                return Results.BadRequest(new { message = error });

            var existe = await _usuariosRepo.EmailExisteAsync(request.Email);
            if (existe)
                return Results.Conflict(new { message = "Ya existe un usuario registrado con este email." });

            var passwordHash = PasswordHasher.HashPassword(request.Password);
            var idUsuario    = await _usuariosRepo.CrearUsuarioLocalAsync(
                request.Email,
                request.Nombre,
                passwordHash
            );

            var token    = TokenGenerator.GenerateToken(32);
            var expiraEn = DateTime.UtcNow.AddHours(
                _config.GetValue<int>("Tokens:EmailVerificationExpirationHours", 24));

            await _verifRepo.CrearTokenVerificacionAsync(idUsuario, token, expiraEn);

            var baseUrl          = _config["App:BaseUrl"];
            var verificationLink = $"{baseUrl}/auth/verify-email/{token}";

            await _emailService.SendVerificationEmailAsync(request.Email, verificationLink);
            _logger.LogInformation("Usuario registrado: {Email}", request.Email);

            // En desarrollo, devolvemos el link en la respuesta para poder probarlo sin correo real.
            if (_env.IsDevelopment())
            {
                return Results.Created("/auth/register", new
                {
                    message             = "Usuario registrado. Revisa tu correo para verificar la cuenta.",
                    verificar_url_dev   = verificationLink
                });
            }

            return Results.Created("/auth/register", new
            {
                message = "Usuario registrado. Revisa tu correo para verificar la cuenta."
            });
        }

        // ── Login ─────────────────────────────────────────────────────────────────

        public async Task<IResult> LoginAsync(LoginRequest request, string? userAgent, string? ip)
        {
            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest(new { message = "El email y la contraseña son obligatorios." });
            }

            // Verificar si la cuenta está bloqueada por intentos fallidos.
            var bloqueadoHasta = await _intentosRepo.ObtenerBloqueoActivoAsync(request.Email);
            if (bloqueadoHasta.HasValue)
            {
                var minutosRestantes = (int)Math.Ceiling((bloqueadoHasta.Value - DateTime.UtcNow).TotalMinutes);
                _logger.LogWarning("Login bloqueado para {Email} hasta {Hasta}", request.Email, bloqueadoHasta.Value);
                return Results.BadRequest(new
                {
                    message = $"Cuenta bloqueada temporalmente por múltiples intentos fallidos. Intenta nuevamente en {minutosRestantes} minutos."
                });
            }

            var usuario = await _usuariosRepo.ObtenerUsuarioPorEmailAsync(request.Email);

            // Mensaje genérico para no filtrar si el email existe o no (user enumeration).
            const string credencialesInvalidas = "No es posible iniciar sesión con las credenciales proporcionadas.";

            if (usuario == null)
            {
                await _intentosRepo.RegistrarIntentoFallidoAsync(
                    request.Email, ip ?? "desconocida",
                    _config.GetValue<int>("Lockout:MaxIntentos", 5),
                    _config.GetValue<int>("Lockout:MinutosBloqueo", 15));
                return Results.BadRequest(new { message = credencialesInvalidas });
            }

            if (!PasswordHasher.VerifyPassword(request.Password, usuario.PasswordHash))
            {
                await _intentosRepo.RegistrarIntentoFallidoAsync(
                    request.Email, ip ?? "desconocida",
                    _config.GetValue<int>("Lockout:MaxIntentos", 5),
                    _config.GetValue<int>("Lockout:MinutosBloqueo", 15));
                return Results.BadRequest(new { message = credencialesInvalidas });
            }

            // Login exitoso — limpiar registro de intentos fallidos.
            await _intentosRepo.LimpiarIntentosAsync(request.Email);

            if (usuario.EmailVerificado == 0)
                return Results.BadRequest(new { message = "Debes verificar tu email antes de iniciar sesión." });

            var refreshToken    = TokenGenerator.GenerateToken(64);
            var refreshExpiraEn = DateTime.UtcNow.AddDays(
                _config.GetValue<int>("Tokens:RefreshTokenExpirationDays", 7));

            // El repositorio hashea el token internamente — se pasa en texto plano.
            var idSesion = await _sesionesRepo.CrearSesionAsync(
                usuario.IdUsuario,
                refreshToken,
                refreshExpiraEn,
                userAgent,
                ip
            );

            var accessToken = _jwtGenerator.GenerateJwt(usuario, idSesion);

            // Limitar sesiones activas por usuario (configurable, default 4).
            var maxSesiones = _config.GetValue<int>("Sesiones:MaxActivasPorUsuario", 4);
            await _sesionesRepo.LimitarSesionesActivasAsync(usuario.IdUsuario, maxSesiones);

            _logger.LogInformation("Login exitoso para usuario {IdUsuario}", usuario.IdUsuario);

            // Notificar al usuario sobre el nuevo login para que detecte accesos no autorizados.
            // Se dispara sin await para no bloquear la respuesta — si falla, solo se loguea.
            _ = _emailService.SendNewLoginNotificationAsync(
                    usuario.Email,
                    ip ?? "desconocida",
                    userAgent ?? "desconocido")
                .ContinueWith(t => _logger.LogWarning(t.Exception, "Error al enviar notificación de login."),
                    TaskContinuationOptions.OnlyOnFaulted);

            return Results.Ok(new
            {
                message = "Login exitoso",
                usuario = new
                {
                    usuario.IdUsuario,
                    usuario.Email,
                    usuario.Nombre
                },
                tokens = new
                {
                    accessToken,
                    accessTokenExpiresInMinutes = _jwtGenerator.ExpirationMinutes,
                    refreshToken,
                    refreshTokenExpiresAt = refreshExpiraEn
                }
            });
        }

        // ── Verificación de email ─────────────────────────────────────────────────

        public async Task<IResult> VerifyEmailAsync(string token)
        {
            var data = await _verifRepo.ObtenerTokenAsync(token);

            if (data is null)
                return Results.BadRequest(new { message = "Token inválido o ya utilizado." });

            var (idUsuario, expiraEn) = data.Value;

            if (expiraEn < DateTime.UtcNow)
                return Results.BadRequest(new { message = "El token ha expirado. Solicita uno nuevo." });

            await _usuariosRepo.VerificarEmailAsync(idUsuario);
            await _verifRepo.InvalidarTokenAsync(token);

            _logger.LogInformation("Email verificado para usuario {IdUsuario}", idUsuario);

            return Results.Ok(new { message = "Email verificado correctamente. Ya puedes iniciar sesión." });
        }

        // ── Forgot password ───────────────────────────────────────────────────────

        public async Task<IResult> ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return Results.BadRequest(new { message = "El email es obligatorio." });

            if (!EsEmailValido(request.Email))
                return Results.BadRequest(new { message = "El formato del email no es válido." });

            // Respuesta siempre igual por seguridad (no revelar si el email existe).
            const string respuestaGenerica = "Si el correo está registrado, recibirás instrucciones para recuperar tu contraseña.";

            var usuario = await _usuariosRepo.ObtenerUsuarioPorEmailAsync(request.Email);
            if (usuario == null)
                return Results.Ok(new { message = respuestaGenerica });

            var token    = TokenGenerator.GenerateToken(32);
            var expiraEn = DateTime.UtcNow.AddHours(
                _config.GetValue<int>("Tokens:PasswordResetExpirationHours", 1));

            await _resetRepo.CrearTokenResetAsync(usuario.IdUsuario, token, expiraEn);

            var baseUrl   = _config["App:BaseUrl"];
            var resetLink = $"{baseUrl}/auth/reset-password/{token}";

            await _emailService.SendPasswordResetEmailAsync(request.Email, resetLink);
            _logger.LogInformation("Token de reset enviado para usuario {IdUsuario}", usuario.IdUsuario);

            if (_env.IsDevelopment())
            {
                return Results.Ok(new
                {
                    message       = respuestaGenerica,
                    reset_url_dev = resetLink
                });
            }

            return Results.Ok(new { message = respuestaGenerica });
        }

        // ── Reset password ────────────────────────────────────────────────────────

        public async Task<IResult> ResetPasswordAsync(ResetPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Token) ||
                string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return Results.BadRequest(new { message = "El token y la nueva contraseña son obligatorios." });
            }

            var (isValid, error) = PasswordPolicy.Validate(request.NewPassword);
            if (!isValid)
                return Results.BadRequest(new { message = error });

            var tokenInfo = await _resetRepo.ObtenerTokenValidoAsync(request.Token);

            if (tokenInfo == null)
                return Results.BadRequest(new { message = "El token es inválido o ya no está disponible." });

            if (tokenInfo.ExpiraEn < DateTime.UtcNow)
                return Results.BadRequest(new { message = "El token de recuperación ha expirado." });

            var newHash = PasswordHasher.HashPassword(request.NewPassword);
            await _usuariosRepo.ActualizarPasswordAsync(tokenInfo.IdUsuario, newHash);
            await _resetRepo.MarcarTokenComoUsadoAsync(tokenInfo.IdReset);

            _logger.LogInformation("Contraseña reseteada para usuario {IdUsuario}", tokenInfo.IdUsuario);

            return Results.Ok(new { message = "Tu contraseña ha sido actualizada correctamente." });
        }

        // ── Refresh token ─────────────────────────────────────────────────────────

        public async Task<IResult> RefreshTokenAsync(RefreshTokenRequest request, string? userAgent, string? ip)
        {
            if (string.IsNullOrWhiteSpace(request.RefreshToken))
                return Results.BadRequest(new { message = "El refresh token es obligatorio." });

            // Hasheamos el token recibido para buscarlo en la BD (donde se almacena el hash).
            var hash  = TokenGenerator.HashToken(request.RefreshToken);
            var sesion = await _sesionesRepo.ObtenerSesionActivaPorHashAsync(hash);

            if (sesion == null)
            {
                // Verificar si el hash existe con estado=0 — significa que el token ya fue
                // usado y alguien lo está reutilizando (posible robo). En ese caso,
                // invalidamos TODAS las sesiones del usuario como medida de contención.
                var sesionRevocada = await _sesionesRepo.ObtenerSesionPorHashAsync(hash);
                if (sesionRevocada != null)
                {
                    await _sesionesRepo.InvalidarTodasPorUsuarioAsync(sesionRevocada.IdUsuario);
                    _logger.LogWarning(
                        "Refresh token reutilizado para usuario {IdUsuario}. Todas las sesiones revocadas.",
                        sesionRevocada.IdUsuario);
                }
                return Results.BadRequest(new { message = "El token ya no es válido." });
            }

            if (sesion.ExpiraEn < DateTime.UtcNow)
            {
                await _sesionesRepo.InvalidarSesionPorHashAsync(hash);
                return Results.BadRequest(new { message = "El refresh token ha expirado." });
            }

            if (sesion.Estado != 1)
                return Results.BadRequest(new { message = "Sesión inválida." });

            var usuario = await _usuariosRepo.ObtenerUsuarioPorIdAsync(sesion.IdUsuario);
            if (usuario == null || usuario.Estado == 0)
                return Results.BadRequest(new { message = "El usuario asociado ya no está disponible." });

            // Rotación segura: invalidar sesión actual antes de crear una nueva.
            await _sesionesRepo.InvalidarSesionPorHashAsync(hash);

            var nuevoRefreshToken = TokenGenerator.GenerateToken(64);
            var expiraEn          = DateTime.UtcNow.AddDays(
                _config.GetValue<int>("Tokens:RefreshTokenExpirationDays", 7));

            // El repositorio hashea internamente — se pasa en texto plano.
            var nuevoIdSesion = await _sesionesRepo.CrearSesionAsync(
                usuario.IdUsuario,
                nuevoRefreshToken,
                expiraEn,
                userAgent,
                ip
            );

            var nuevoAccessToken = _jwtGenerator.GenerateJwt(usuario, nuevoIdSesion);

            _logger.LogInformation("Refresh token rotado para usuario {IdUsuario}", usuario.IdUsuario);

            return Results.Ok(new
            {
                access_token  = nuevoAccessToken,
                refresh_token = nuevoRefreshToken,
                expires_in    = _jwtGenerator.ExpirationMinutes * 60,
                token_type    = "Bearer"
            });
        }

        // ── Change password ───────────────────────────────────────────────────────

        public async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request, long idUsuario)
        {
            var usuario = await _usuariosRepo.ObtenerUsuarioPorIdAsync(idUsuario);
            if (usuario == null)
                return Results.BadRequest(new { message = "El usuario no está disponible." });

            if (!PasswordHasher.VerifyPassword(request.PasswordActual, usuario.PasswordHash))
                return Results.BadRequest(new { message = "La contraseña actual es incorrecta." });

            if (PasswordHasher.VerifyPassword(request.PasswordNueva, usuario.PasswordHash))
                return Results.BadRequest(new { message = "La nueva contraseña no puede ser igual a la anterior." });

            var (isValid, error) = PasswordPolicy.Validate(request.PasswordNueva);
            if (!isValid)
                return Results.BadRequest(new { message = error });

            var hashNuevo = PasswordHasher.HashPassword(request.PasswordNueva);
            await _usuariosRepo.ActualizarPasswordAsync(usuario.IdUsuario, hashNuevo);

            // Invalida todas las sesiones por seguridad — obliga a hacer login de nuevo.
            await _sesionesRepo.InvalidarTodasPorUsuarioAsync(usuario.IdUsuario);

            // Notificar al usuario sobre el cambio de contraseña.
            _ = _emailService.SendPasswordChangedNotificationAsync(usuario.Email)
                .ContinueWith(t => _logger.LogWarning(t.Exception, "Error al enviar notificación de cambio de contraseña."),
                    TaskContinuationOptions.OnlyOnFaulted);

            _logger.LogInformation("Contraseña cambiada para usuario {IdUsuario}", idUsuario);

            return Results.Ok(new { message = "Tu contraseña ha sido actualizada. Por seguridad, vuelve a iniciar sesión." });
        }

        // ── Logout ────────────────────────────────────────────────────────────────

        public async Task<IResult> LogoutAsync(long idSesion)
        {
            var exito = await _sesionesRepo.InvalidarSesionPorIdAsync(idSesion);

            return Results.Ok(new
            {
                message = exito
                    ? "Sesión cerrada correctamente."
                    : "La sesión ya estaba cerrada o no existe."
            });
        }

        // ── Logout all ────────────────────────────────────────────────────────────

        public async Task<IResult> LogoutAllAsync(long idUsuario)
        {
            var cantidad = await _sesionesRepo.InvalidarTodasPorUsuarioAsync(idUsuario);

            _logger.LogInformation("Logout all: {Cantidad} sesiones cerradas para usuario {IdUsuario}", cantidad, idUsuario);

            return Results.Ok(new { message = $"Todas las sesiones han sido cerradas ({cantidad})." });
        }

        // ── Get sessions ──────────────────────────────────────────────────────────

        public async Task<IResult> GetSessionsAsync(long idUsuario)
        {
            var sesiones = await _sesionesRepo.ObtenerSesionesActivasPorUsuarioAsync(idUsuario);

            return Results.Ok(new
            {
                // Se excluye TokenHash para no exponer el hash del refresh token al cliente.
                sesiones = sesiones.Select(s => new
                {
                    s.IdSesion,
                    s.UserAgent,
                    s.IpOrigen,
                    s.Creacion,
                    s.ExpiraEn
                })
            });
        }

        // ── Revoke session ────────────────────────────────────────────────────────

        public async Task<IResult> RevokeSessionAsync(long idSesion, long idUsuario)
        {
            var sesion = await _sesionesRepo.ObtenerSesionPorIdAsync(idSesion);

            // Validar que la sesión exista y pertenezca al usuario autenticado.
            if (sesion == null || sesion.IdUsuario != idUsuario)
                return Results.BadRequest(new { message = "No puedes revocar esta sesión." });

            await _sesionesRepo.InvalidarSesionPorIdAsync(idSesion);

            _logger.LogInformation("Sesión {IdSesion} revocada por usuario {IdUsuario}", idSesion, idUsuario);

            return Results.Ok(new { message = "Sesión revocada correctamente." });
        }

        // ── Google Login ──────────────────────────────────────────────────────────

        /// <summary>
        /// Autentica un usuario con un ID Token de Google.
        ///
        /// Flujo:
        /// 1. Validar el ID Token contra las claves públicas de Google.
        /// 2. Extraer email, nombre, foto y google_sub del payload.
        /// 3. Buscar usuario por google_sub → si existe, login directo.
        /// 4. Buscar usuario por email → si existe, vincular google_sub y login.
        /// 5. Si no existe → crear cuenta nueva con proveedor 'GOOGLE'.
        /// 6. Retornar el mismo par de tokens que el login local.
        ///
        /// Configuración requerida: Google:ClientId en appsettings.json.
        /// </summary>
        public async Task<IResult> GoogleLoginAsync(string idToken, string? userAgent, string? ip)
        {
            if (string.IsNullOrWhiteSpace(idToken))
                return Results.BadRequest(new { message = "El ID Token de Google es obligatorio." });

            Google.Apis.Auth.GoogleJsonWebSignature.Payload payload;
            try
            {
                var settings = new Google.Apis.Auth.GoogleJsonWebSignature.ValidationSettings
                {
                    Audience = [_config["Google:ClientId"]!]
                };
                payload = await Google.Apis.Auth.GoogleJsonWebSignature.ValidateAsync(idToken, settings);
            }
            catch (Google.Apis.Auth.InvalidJwtException ex)
            {
                _logger.LogWarning("ID Token de Google inválido: {Error}", ex.Message);
                return Results.BadRequest(new { message = "El token de Google no es válido o ha expirado." });
            }

            var googleSub = payload.Subject;
            var email     = payload.Email;
            var nombre    = payload.Name;
            var fotoUrl   = payload.Picture;

            // Paso 1: buscar por google_sub (usuario ya vinculó Google antes)
            var usuario = await _usuariosRepo.ObtenerUsuarioPorGoogleSubAsync(googleSub);

            if (usuario == null)
            {
                // Paso 2: buscar por email (puede tener cuenta local con el mismo email)
                var usuarioExistente = await _usuariosRepo.ObtenerUsuarioPorEmailAsync(email);
                if (usuarioExistente != null)
                {
                    // Vincular Google a la cuenta local existente
                    await _usuariosRepo.VincularGoogleSubAsync(usuarioExistente.IdUsuario, googleSub, fotoUrl);
                    usuario = usuarioExistente;
                    _logger.LogInformation("Google vinculado a cuenta existente para usuario {IdUsuario}", usuario.IdUsuario);
                }
                else
                {
                    // Paso 3: crear nueva cuenta Google
                    var idUsuario = await _usuariosRepo.CrearUsuarioGoogleAsync(email, nombre, fotoUrl, googleSub);
                    usuario = await _usuariosRepo.ObtenerUsuarioPorIdAsync(idUsuario);
                    _logger.LogInformation("Nueva cuenta creada via Google para {Email}", email);
                }
            }

            if (usuario == null || usuario.Estado == 0)
                return Results.BadRequest(new { message = "No se pudo procesar la autenticación con Google." });

            var refreshToken    = TokenGenerator.GenerateToken(64);
            var refreshExpiraEn = DateTime.UtcNow.AddDays(_config.GetValue<int>("Tokens:RefreshTokenExpirationDays", 7));

            var idSesion = await _sesionesRepo.CrearSesionAsync(
                usuario.IdUsuario, refreshToken, refreshExpiraEn, userAgent, ip);

            var accessToken = _jwtGenerator.GenerateJwt(usuario, idSesion);
            await _sesionesRepo.LimitarSesionesActivasAsync(
                usuario.IdUsuario, _config.GetValue<int>("Sesiones:MaxActivasPorUsuario", 4));

            _logger.LogInformation("Login Google exitoso para usuario {IdUsuario}", usuario.IdUsuario);

            return Results.Ok(new
            {
                message = "Login con Google exitoso",
                usuario = new { usuario.IdUsuario, usuario.Email, usuario.Nombre },
                tokens  = new
                {
                    accessToken,
                    accessTokenExpiresInMinutes = _jwtGenerator.ExpirationMinutes,
                    refreshToken,
                    refreshTokenExpiresAt = refreshExpiraEn
                }
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Valida el formato del email usando la clase MailAddress de .NET,
        /// que implementa validación RFC 5322. Más confiable que una regex manual.
        /// </summary>
        private static bool EsEmailValido(string email)
        {
            try
            {
                _ = new System.Net.Mail.MailAddress(email);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
