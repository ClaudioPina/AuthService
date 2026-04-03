using AuthService.Api.Data;
using AuthService.Api.Dtos.Auth;
using AuthService.Api.Repositories;
using AuthService.Api.Utils;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

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
        private readonly IDistributedCache _cache;
        private readonly AuthMetrics _metrics;
        private readonly AppDbContext _db;
        private readonly AuditoriaRepository _auditoriaRepo;
        private readonly IHibpService _hibp;
        private const string PasswordChangeCachePrefix = "pwdchange:";
        private const int PasswordChangeConfirmationMinutes = 30;

        private sealed record PendingPasswordChange(long UserId, string NewPasswordHash);

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
            ILogger<AutenticacionService> logger,
            IDistributedCache cache,
            AuthMetrics metrics,
            AppDbContext db,
            AuditoriaRepository auditoriaRepo,
            IHibpService hibp)
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
            _cache         = cache;
            _metrics       = metrics;
            _db            = db;
            _auditoriaRepo = auditoriaRepo;
            _hibp          = hibp;
        }

        // ── Registro ─────────────────────────────────────────────────────────────

        public async Task<IResult> RegisterAsync(RegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.PasswordConfirmacion))
            {
                _metrics.RecordRegistration("validation_error");
                return Results.BadRequest(new { message = "El email, la contraseña y su confirmación son obligatorios." });
            }

            if (!string.Equals(request.Password, request.PasswordConfirmacion, StringComparison.Ordinal))
            {
                _metrics.RecordRegistration("validation_error");
                return Results.BadRequest(new { message = "Las contraseñas no coinciden." });
            }

            if (!EsEmailValido(request.Email))
            {
                _metrics.RecordRegistration("validation_error");
                return Results.BadRequest(new { message = "El formato del email no es válido." });
            }

            var (isValid, error) = PasswordPolicy.Validate(request.Password);
            if (!isValid)
            {
                _metrics.RecordRegistration("validation_error");
                return Results.BadRequest(new { message = error });
            }

            if (await _hibp.EsPasswordCompromisedAsync(request.Password))
            {
                _metrics.RecordRegistration("validation_error");
                return Results.BadRequest(new { message = "Esta contraseña aparece en filtraciones de datos conocidas. Por favor elige una diferente." });
            }

            // Normalizar email: trim + lowercase para consistencia en BD.
            // Evita que 'User@Mail.COM' y 'user@mail.com' coexistan como cuentas distintas.
            var email = request.Email.Trim().ToLowerInvariant();

            var existe = await _usuariosRepo.EmailExisteAsync(email);
            if (existe)
            {
                _metrics.RecordRegistration("conflict");
                return Results.Conflict(new { message = "Ya existe un usuario registrado con este email." });
            }

            var passwordHash = PasswordHasher.HashPassword(request.Password);
            var idUsuario    = await _usuariosRepo.CrearUsuarioLocalAsync(
                email,
                request.Nombre,
                passwordHash
            );

            var token      = TokenGenerator.GenerateToken(32);
            var tokenHash  = TokenGenerator.HashToken(token);   // se almacena el hash, no el token plano
            var expiraEn   = DateTime.UtcNow.AddHours(
                _config.GetValue<int>("Tokens:EmailVerificationExpirationHours", 24));

            await _verifRepo.CrearTokenVerificacionAsync(idUsuario, tokenHash, expiraEn);

            var baseUrl          = _config["App:BaseUrl"];
            var verificationLink = $"{baseUrl}/auth/verify-email/{token}"; // link lleva el token plano

            await _emailService.SendVerificationEmailAsync(email, verificationLink);
            _metrics.RecordRegistration("success");
            _logger.LogInformation("Usuario registrado: {Email}", email);

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

            var email = request.Email.Trim().ToLowerInvariant();

            // Verificar si la cuenta está bloqueada por intentos fallidos.
            var bloqueadoHasta = await _intentosRepo.ObtenerBloqueoActivoAsync(email);
            if (bloqueadoHasta.HasValue)
            {
                var minutosRestantes = (int)Math.Ceiling((bloqueadoHasta.Value - DateTime.UtcNow).TotalMinutes);
                _logger.LogWarning("Login bloqueado para {Email} hasta {Hasta}", email, bloqueadoHasta.Value);
                _metrics.RecordLogin("blocked");
                return Results.BadRequest(new
                {
                    message = $"Cuenta bloqueada temporalmente por múltiples intentos fallidos. Intenta nuevamente en {minutosRestantes} minutos."
                });
            }

            var usuario = await _usuariosRepo.ObtenerUsuarioPorEmailAsync(email);

            // Mensaje genérico para no filtrar si el email existe o no (user enumeration).
            const string credencialesInvalidas = "No es posible iniciar sesión con las credenciales proporcionadas.";

            if (usuario == null)
            {
                await _intentosRepo.RegistrarIntentoFallidoAsync(
                    email, ip ?? "desconocida",
                    _config.GetValue<int>("Lockout:MaxIntentos", 5),
                    _config.GetValue<int>("Lockout:MinutosBloqueo", 15));
                _metrics.RecordLogin("invalid_credentials");
                return Results.BadRequest(new { message = credencialesInvalidas });
            }

            // Cuenta creada exclusivamente con Google — no tiene password local.
            // Retornar un error específico en lugar de lanzar una excepción al comparar con null.
            if (usuario.PasswordHash == null)
            {
                _metrics.RecordLogin("invalid_credentials");
                return Results.BadRequest(new { message = "Esta cuenta está vinculada a Google. Usa el botón 'Iniciar sesión con Google'." });
            }

            if (!PasswordHasher.VerifyPassword(request.Password, usuario.PasswordHash))
            {
                await _intentosRepo.RegistrarIntentoFallidoAsync(
                    email, ip ?? "desconocida",
                    _config.GetValue<int>("Lockout:MaxIntentos", 5),
                    _config.GetValue<int>("Lockout:MinutosBloqueo", 15));
                _metrics.RecordLogin("invalid_credentials");
                return Results.BadRequest(new { message = credencialesInvalidas });
            }

            // Login exitoso — limpiar registro de intentos fallidos.
            await _intentosRepo.LimpiarIntentosAsync(email);

            if (usuario.EmailVerificado == 0)
            {
                _metrics.RecordLogin("email_not_verified");
                return Results.BadRequest(new { message = "Debes verificar tu email antes de iniciar sesión." });
            }

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
            // Los IDs retornados se usan para limpiar el cache antes de que expire el TTL.
            var maxSesiones = _config.GetValue<int>("Sesiones:MaxActivasPorUsuario", 4);
            var sesionesDesactivadas = await _sesionesRepo.LimitarSesionesActivasAsync(usuario.IdUsuario, maxSesiones);
            foreach (var id in sesionesDesactivadas)
                await RemoveSesionCacheAsync(id);

            _metrics.RecordLogin("success");
            _logger.LogInformation("Login exitoso para usuario {IdUsuario}", usuario.IdUsuario);

            // Registrar en auditoría (fire-and-forget: un fallo de auditoría no bloquea el login).
            _ = _auditoriaRepo.RegistrarAsync(usuario.IdUsuario, "LOGIN", ip, userAgent)
                .ContinueWith(t => _logger.LogWarning(t.Exception, "Error al registrar auditoría de login."),
                    TaskContinuationOptions.OnlyOnFaulted);

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
            // El token viaja en texto plano en la URL; se hashea para buscar en BD.
            var tokenHash = TokenGenerator.HashToken(token);
            var data = await _verifRepo.ObtenerTokenAsync(tokenHash);

            if (data is null)
                return Results.BadRequest(new { message = "Token inválido o ya utilizado." });

            var (idUsuario, expiraEn) = data.Value;

            if (expiraEn < DateTime.UtcNow)
                return Results.BadRequest(new { message = "El token ha expirado. Solicita uno nuevo." });

            await _usuariosRepo.VerificarEmailAsync(idUsuario);
            await _verifRepo.InvalidarTokenAsync(tokenHash);

            _logger.LogInformation("Email verificado para usuario {IdUsuario}", idUsuario);

            return Results.Ok(new { message = "Email verificado correctamente. Ya puedes iniciar sesión." });
        }

        // ── Resend verification ───────────────────────────────────────────────────

        /// <summary>
        /// Reenvía el email de verificación. Siempre responde con el mismo mensaje
        /// para evitar enumerar si el email existe (mismo patrón que ForgotPassword).
        /// </summary>
        public async Task<IResult> ResendVerificationAsync(ResendVerificationRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || !EsEmailValido(request.Email))
                return Results.Ok(new { message = "Si el email está registrado y sin verificar, recibirás un nuevo enlace en breve." });

            var email   = request.Email.Trim().ToLowerInvariant();
            var usuario = await _usuariosRepo.ObtenerUsuarioPorEmailAsync(email);

            // Respuesta idéntica si no existe o ya está verificado — previene enumeración.
            if (usuario == null || usuario.EmailVerificado == 1)
                return Results.Ok(new { message = "Si el email está registrado y sin verificar, recibirás un nuevo enlace en breve." });

            // Invalidar tokens anteriores antes de generar uno nuevo.
            await _verifRepo.InvalidarTokensAnterioresAsync(usuario.IdUsuario);

            var token      = TokenGenerator.GenerateToken(32);
            var tokenHash  = TokenGenerator.HashToken(token);
            var expiraEn   = DateTime.UtcNow.AddHours(
                _config.GetValue<int>("Tokens:EmailVerificationExpirationHours", 24));

            await _verifRepo.CrearTokenVerificacionAsync(usuario.IdUsuario, tokenHash, expiraEn);

            var baseUrl          = _config["App:BaseUrl"];
            var verificationLink = $"{baseUrl}/auth/verify-email/{token}";

            await _emailService.SendVerificationEmailAsync(email, verificationLink);
            _logger.LogInformation("Email de verificación reenviado para usuario {IdUsuario}", usuario.IdUsuario);

            if (_env.IsDevelopment())
                return Results.Ok(new
                {
                    message              = "Si el email está registrado y sin verificar, recibirás un nuevo enlace en breve.",
                    verificar_url_dev    = verificationLink
                });

            return Results.Ok(new { message = "Si el email está registrado y sin verificar, recibirás un nuevo enlace en breve." });
        }

        // ── Forgot password ───────────────────────────────────────────────────────

        public async Task<IResult> ForgotPasswordAsync(ForgotPasswordRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email))
                return Results.BadRequest(new { message = "El email es obligatorio." });

            if (!EsEmailValido(request.Email))
                return Results.BadRequest(new { message = "El formato del email no es válido." });

            var email = request.Email.Trim().ToLowerInvariant();

            // Respuesta siempre igual por seguridad (no revelar si el email existe).
            const string respuestaGenerica = "Si el correo está registrado, recibirás instrucciones para recuperar tu contraseña.";

            var usuario = await _usuariosRepo.ObtenerUsuarioPorEmailAsync(email);
            if (usuario == null)
                return Results.Ok(new { message = respuestaGenerica });

            var token      = TokenGenerator.GenerateToken(32);
            var tokenHash  = TokenGenerator.HashToken(token);   // se almacena el hash, no el token plano
            var expiraEn   = DateTime.UtcNow.AddHours(
                _config.GetValue<int>("Tokens:PasswordResetExpirationHours", 1));

            await _resetRepo.CrearTokenResetAsync(usuario.IdUsuario, tokenHash, expiraEn);

            var baseUrl   = _config["App:BaseUrl"];
            var resetLink = $"{baseUrl}/auth/reset-password/{token}"; // link lleva el token plano

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

            // El token viaja en texto plano en la URL; se hashea para buscar en BD.
            var tokenHash = TokenGenerator.HashToken(request.Token);
            var tokenInfo = await _resetRepo.ObtenerTokenValidoAsync(tokenHash);

            if (tokenInfo == null)
                return Results.BadRequest(new { message = "El token es inválido o ya no está disponible." });

            if (tokenInfo.ExpiraEn < DateTime.UtcNow)
                return Results.BadRequest(new { message = "El token de recuperación ha expirado." });

            var newHash = PasswordHasher.HashPassword(request.NewPassword);

            // Actualizar password y marcar token como usado en una sola transacción.
            // Si falla marcar el token, el password no queda actualizado → evita token reutilizable.
            var (txConn, tx) = await _db.BeginTransactionAsync();
            try
            {
                await _usuariosRepo.ActualizarPasswordAsync(tokenInfo.IdUsuario, newHash, txConn, tx);
                await _resetRepo.MarcarTokenComoUsadoAsync(tokenInfo.IdReset, txConn, tx);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
            finally
            {
                await tx.DisposeAsync();
                await txConn.DisposeAsync();
            }

            // Revocar todas las sesiones activas: quien reseteó la contraseña puede haber
            // sido comprometido, así que forzamos re-login desde cero.
            var sesionesActivas = await _sesionesRepo.ObtenerSesionesActivasPorUsuarioAsync(tokenInfo.IdUsuario);
            foreach (var s in sesionesActivas)
                await RemoveSesionCacheAsync(s.IdSesion);
            await _sesionesRepo.InvalidarTodasPorUsuarioAsync(tokenInfo.IdUsuario);

            // Notificar al usuario — fire-and-forget, no bloquea la respuesta.
            var usuario = await _usuariosRepo.ObtenerUsuarioPorIdAsync(tokenInfo.IdUsuario);
            if (usuario != null)
            {
                _ = _emailService.SendPasswordChangedNotificationAsync(usuario.Email)
                    .ContinueWith(t => _logger.LogWarning(t.Exception, "Error al enviar notificación post-reset."),
                        TaskContinuationOptions.OnlyOnFaulted);
            }

            _logger.LogInformation("Contraseña reseteada para usuario {IdUsuario}", tokenInfo.IdUsuario);

            _ = _auditoriaRepo.RegistrarAsync(tokenInfo.IdUsuario, "RESET_CONTRASENA", null, null)
                .ContinueWith(t => _logger.LogWarning(t.Exception, "Error al registrar auditoría de reset."),
                    TaskContinuationOptions.OnlyOnFaulted);

            return Results.Ok(new { message = "Tu contraseña ha sido actualizada. Por seguridad, vuelve a iniciar sesión." });
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
                    // Invalidar cache de todas las sesiones activas antes de revocarlas en BD.
                    var todasSesiones = await _sesionesRepo.ObtenerSesionesActivasPorUsuarioAsync(sesionRevocada.IdUsuario);
                    foreach (var s in todasSesiones)
                        await RemoveSesionCacheAsync(s.IdSesion);

                    await _sesionesRepo.InvalidarTodasPorUsuarioAsync(sesionRevocada.IdUsuario);
                    _metrics.RecordTokenRefresh("reuse_detected");
                    _logger.LogWarning(
                        "Refresh token reutilizado para usuario {IdUsuario}. Todas las sesiones revocadas.",
                        sesionRevocada.IdUsuario);
                }
                else
                {
                    _metrics.RecordTokenRefresh("invalid");
                }
                return Results.BadRequest(new { message = "El token ya no es válido." });
            }

            if (sesion.ExpiraEn < DateTime.UtcNow)
            {
                await RemoveSesionCacheAsync(sesion.IdSesion);
                await _sesionesRepo.InvalidarSesionPorHashAsync(hash);
                _metrics.RecordTokenRefresh("expired");
                return Results.BadRequest(new { message = "El refresh token ha expirado." });
            }

            if (sesion.Estado != 1)
            {
                _metrics.RecordTokenRefresh("invalid");
                return Results.BadRequest(new { message = "Sesión inválida." });
            }

            var usuario = await _usuariosRepo.ObtenerUsuarioPorIdAsync(sesion.IdUsuario);
            if (usuario == null || usuario.Estado == 0)
                return Results.BadRequest(new { message = "El usuario asociado ya no está disponible." });

            // Rotación atómica: invalidar sesión vieja y crear la nueva en la misma transacción.
            // Si falla la creación, el rollback restaura la sesión anterior → el usuario no queda bloqueado.
            await RemoveSesionCacheAsync(sesion.IdSesion);

            var nuevoRefreshToken = TokenGenerator.GenerateToken(64);
            var expiraEn          = DateTime.UtcNow.AddDays(
                _config.GetValue<int>("Tokens:RefreshTokenExpirationDays", 7));

            long nuevoIdSesion;
            var (txConn, tx) = await _db.BeginTransactionAsync();
            try
            {
                await _sesionesRepo.InvalidarSesionPorHashAsync(hash, txConn, tx);
                // El repositorio hashea internamente — se pasa en texto plano.
                nuevoIdSesion = await _sesionesRepo.CrearSesionAsync(
                    usuario.IdUsuario, nuevoRefreshToken, expiraEn, userAgent, ip, txConn, tx);
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
            finally
            {
                await tx.DisposeAsync();
                await txConn.DisposeAsync();
            }

            var nuevoAccessToken = _jwtGenerator.GenerateJwt(usuario, nuevoIdSesion);

            _metrics.RecordTokenRefresh("success");
            _logger.LogInformation("Refresh token rotado para usuario {IdUsuario}", usuario.IdUsuario);

            return Results.Ok(new
            {
                tokens = new
                {
                    accessToken                 = nuevoAccessToken,
                    accessTokenExpiresInMinutes = _jwtGenerator.ExpirationMinutes,
                    refreshToken                = nuevoRefreshToken,
                    refreshTokenExpiresAt       = expiraEn
                }
            });
        }

        // ── Change password ───────────────────────────────────────────────────────

        public async Task<IResult> ChangePasswordAsync(ChangePasswordRequest request, long idUsuario)
        {
            if (string.IsNullOrWhiteSpace(request.PasswordActual) ||
                string.IsNullOrWhiteSpace(request.PasswordNueva) ||
                string.IsNullOrWhiteSpace(request.PasswordNuevaConfirmacion))
            {
                return Results.BadRequest(new { message = "La contraseña actual, la nueva y su confirmación son obligatorias." });
            }

            if (!string.Equals(request.PasswordNueva, request.PasswordNuevaConfirmacion, StringComparison.Ordinal))
                return Results.BadRequest(new { message = "Las contraseñas nuevas no coinciden." });

            var usuario = await _usuariosRepo.ObtenerUsuarioPorIdAsync(idUsuario);
            if (usuario == null)
                return Results.BadRequest(new { message = "El usuario no está disponible." });

            // Cuentas Google-only no tienen password local — no pueden usar este endpoint.
            if (usuario.PasswordHash == null)
                return Results.BadRequest(new { message = "Esta cuenta no tiene contraseña local. Usa Google para autenticarte." });

            if (!PasswordHasher.VerifyPassword(request.PasswordActual, usuario.PasswordHash))
                return Results.BadRequest(new { message = "La contraseña actual es incorrecta." });

            if (PasswordHasher.VerifyPassword(request.PasswordNueva, usuario.PasswordHash))
                return Results.BadRequest(new { message = "La nueva contraseña no puede ser igual a la anterior." });

            var (isValid, error) = PasswordPolicy.Validate(request.PasswordNueva);
            if (!isValid)
                return Results.BadRequest(new { message = error });

            if (await _hibp.EsPasswordCompromisedAsync(request.PasswordNueva))
                return Results.BadRequest(new { message = "Esta contraseña aparece en filtraciones de datos conocidas. Por favor elige una diferente." });

            var hashNuevo = PasswordHasher.HashPassword(request.PasswordNueva);
            var token = TokenGenerator.GenerateToken(32);
            var tokenHash = TokenGenerator.HashToken(token);
            var expiraEn = DateTime.UtcNow.AddMinutes(PasswordChangeConfirmationMinutes);
            var confirmationLink = $"{_config["App:BaseUrl"]}/auth/confirm-change-password/{token}";

            var payload = JsonSerializer.Serialize(new PendingPasswordChange(usuario.IdUsuario, hashNuevo));
            await _cache.SetStringAsync(
                BuildPasswordChangeCacheKey(tokenHash),
                payload,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpiration = expiraEn
                });

            await _emailService.SendPasswordChangeVerificationEmailAsync(usuario.Email, confirmationLink);

            _logger.LogInformation("Solicitud de cambio de contraseña enviada para usuario {IdUsuario}", idUsuario);

            _ = _auditoriaRepo.RegistrarAsync(idUsuario, "SOLICITUD_CAMBIO_CONTRASENA", null, null)
                .ContinueWith(t => _logger.LogWarning(t.Exception, "Error al registrar auditoría de solicitud de cambio de contraseña."),
                    TaskContinuationOptions.OnlyOnFaulted);

            if (_env.IsDevelopment())
            {
                return Results.Ok(new
                {
                    message = "Te enviamos un correo para confirmar el cambio de contraseña.",
                    confirmar_cambio_url_dev = confirmationLink
                });
            }

            return Results.Ok(new { message = "Te enviamos un correo para confirmar el cambio de contraseña." });
        }

        public async Task<IResult> ConfirmPasswordChangeAsync(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return Results.BadRequest(new { message = "El token de confirmación es obligatorio." });

            var tokenHash = TokenGenerator.HashToken(token);
            var cached = await _cache.GetStringAsync(BuildPasswordChangeCacheKey(tokenHash));
            if (string.IsNullOrWhiteSpace(cached))
                return Results.BadRequest(new { message = "El token es inválido o ha expirado." });

            PendingPasswordChange? pending;
            try
            {
                pending = JsonSerializer.Deserialize<PendingPasswordChange>(cached);
            }
            catch
            {
                pending = null;
            }

            if (pending == null || pending.UserId <= 0 || string.IsNullOrWhiteSpace(pending.NewPasswordHash))
                return Results.BadRequest(new { message = "No se pudo procesar la confirmación del cambio de contraseña." });

            await _cache.RemoveAsync(BuildPasswordChangeCacheKey(tokenHash));
            await _usuariosRepo.ActualizarPasswordAsync(pending.UserId, pending.NewPasswordHash);

            var sesionesActivas = await _sesionesRepo.ObtenerSesionesActivasPorUsuarioAsync(pending.UserId);
            foreach (var s in sesionesActivas)
                await RemoveSesionCacheAsync(s.IdSesion);
            await _sesionesRepo.InvalidarTodasPorUsuarioAsync(pending.UserId);

            var usuario = await _usuariosRepo.ObtenerUsuarioPorIdAsync(pending.UserId);
            if (usuario != null)
            {
                _ = _emailService.SendPasswordChangedNotificationAsync(usuario.Email)
                    .ContinueWith(t => _logger.LogWarning(t.Exception, "Error al enviar notificación de cambio de contraseña confirmado."),
                        TaskContinuationOptions.OnlyOnFaulted);
            }

            _logger.LogInformation("Cambio de contraseña confirmado para usuario {IdUsuario}", pending.UserId);

            _ = _auditoriaRepo.RegistrarAsync(pending.UserId, "CAMBIO_CONTRASENA_CONFIRMADO", null, null)
                .ContinueWith(t => _logger.LogWarning(t.Exception, "Error al registrar auditoría de confirmación de cambio de contraseña."),
                    TaskContinuationOptions.OnlyOnFaulted);

            return Results.Ok(new { message = "Tu contraseña ha sido actualizada. Por seguridad, vuelve a iniciar sesión." });
        }

        // ── Logout ────────────────────────────────────────────────────────────────

        public async Task<IResult> LogoutAsync(long idSesion)
        {
            await RemoveSesionCacheAsync(idSesion);
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
            var sesionesActivas = await _sesionesRepo.ObtenerSesionesActivasPorUsuarioAsync(idUsuario);
            foreach (var s in sesionesActivas)
                await RemoveSesionCacheAsync(s.IdSesion);

            var cantidad = await _sesionesRepo.InvalidarTodasPorUsuarioAsync(idUsuario);

            _logger.LogInformation("Logout all: {Cantidad} sesiones cerradas para usuario {IdUsuario}", cantidad, idUsuario);

            _ = _auditoriaRepo.RegistrarAsync(idUsuario, "LOGOUT_ALL", null, null)
                .ContinueWith(t => _logger.LogWarning(t.Exception, "Error al registrar auditoría de logout all."),
                    TaskContinuationOptions.OnlyOnFaulted);

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

            await RemoveSesionCacheAsync(idSesion);
            await _sesionesRepo.InvalidarSesionPorIdAsync(idSesion);

            _logger.LogInformation("Sesión {IdSesion} revocada por usuario {IdUsuario}", idSesion, idUsuario);

            _ = _auditoriaRepo.RegistrarAsync(idUsuario, "REVOCACION_SESION", null, null)
                .ContinueWith(t => _logger.LogWarning(t.Exception, "Error al registrar auditoría de revocación."),
                    TaskContinuationOptions.OnlyOnFaulted);

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
            var sesionesDesactivadasGoogle = await _sesionesRepo.LimitarSesionesActivasAsync(
                usuario.IdUsuario, _config.GetValue<int>("Sesiones:MaxActivasPorUsuario", 4));
            foreach (var id in sesionesDesactivadasGoogle)
                await RemoveSesionCacheAsync(id);

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

        // ── Perfil ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Devuelve los datos públicos del usuario autenticado.
        /// </summary>
        public async Task<IResult> ObtenerPerfilAsync(long idUsuario)
        {
            var usuario = await _usuariosRepo.ObtenerUsuarioPorIdAsync(idUsuario);

            if (usuario == null)
                return Results.NotFound(new { message = "Usuario no encontrado." });

            return Results.Ok(new PerfilUsuarioResponse
            {
                Id              = usuario.IdUsuario,
                Email           = usuario.Email,
                Nombre          = usuario.Nombre,
                FotoUrl         = usuario.FotoUrl,
                EmailVerificado = usuario.EmailVerificado == 1,
                ProveedorLogin  = usuario.ProveedorLogin,
                Creacion        = usuario.Creacion
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static string BuildPasswordChangeCacheKey(string tokenHash)
            => $"{PasswordChangeCachePrefix}{tokenHash}";

        /// <summary>
        /// Elimina la entrada de cache de una sesión de forma segura.
        /// Los fallos de cache no interrumpen el flujo principal — la BD es la fuente de verdad.
        /// </summary>
        private async Task RemoveSesionCacheAsync(long idSesion)
        {
            try { await _cache.RemoveAsync($"sess:{idSesion}"); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al invalidar cache para sesión {IdSesion}.", idSesion);
            }
        }

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
