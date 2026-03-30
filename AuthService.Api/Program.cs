using AuthService.Api.Repositories;
using AuthService.Api.Data;
using AuthService.Api.Dtos.Auth;
using AuthService.Api.Utils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using AuthService.Api.Middlewares;

var builder = WebApplication.CreateBuilder(args);

// Swagger
builder.Services.AddEndpointsApiExplorer();
// Configuración de Swagger para usar JWT Bearer
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Ingresa tu JWT aquí. Ejemplo: Bearer {token}"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

builder.Services.AddAuthorization();

var jwtKey = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!);
var issuer = builder.Configuration["Jwt:Issuer"];
var audience = builder.Configuration["Jwt:Audience"];

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,

            ValidIssuer = issuer,
            ValidAudience = audience,
            IssuerSigningKey = new SymmetricSecurityKey(jwtKey)
        };
    });


// Dependencias de repositorios
builder.Services.AddScoped<AppDbContext>();
builder.Services.AddScoped<UsuariosRepository>();
builder.Services.AddScoped<VerificacionEmailRepository>();
builder.Services.AddScoped<ResetPasswordRepository>();
builder.Services.AddScoped<SesionesUsuariosRepository>();
builder.Services.AddSingleton<JwtGenerator>();


// Construir la app
var app = builder.Build();

// Swagger en desarrollo
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ValidarSesionMiddleware>();

// Rutas de autenticación
app.MapPost("/auth/register", async (
    RegisterRequest request,
    UsuariosRepository usuariosRepo,
    VerificacionEmailRepository verifRepo) =>
{
    // Validaciones básicas
    if (string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new
        {
            message = "El email y la contraseña son obligatorios."
        });
    }

    // Reglas mínimas de seguridad
    var (isValid, error) = PasswordPolicy.Validate(request.Password);

    if (!isValid)
        return Results.BadRequest(new { message = error });

    // Verificar si el email ya existe
    var existe = await usuariosRepo.EmailExisteAsync(request.Email);
    if (existe)
    {
        return Results.Conflict(new
        {
            message = "Ya existe un usuario registrado con este email."
        });
    }

    // Hash de la contraseña
    var passwordHash = PasswordHasher.HashPassword(request.Password);

    // Crear usuario en la BD
    var idUsuario = await usuariosRepo.CrearUsuarioLocalAsync(
        request.Email,
        request.Nombre,
        passwordHash
    );

    // Crear token de verificación en VERIFICACION_EMAIL
    var token = TokenGenerator.GenerateToken(32); // 64 caracteres hex
    var expiraEn = DateTime.UtcNow.AddHours(24);

    await verifRepo.CrearTokenVerificacionAsync(idUsuario, token, expiraEn);

    // Construir URL de verificación (por ahora solo la retornamos como texto)
    var baseUrl = "https://localhost:5001"; // luego lo movemos a appsettings
    var verificationLink = $"{baseUrl}/auth/verify-email/{token}";

    // Más adelante, este link se enviará por correo.
    return Results.Created("/auth/register", new
    {
        message = "Usuario registrado. Revisa tu correo para verificar la cuenta.",
        verificar_url_demo = verificationLink
    });
})

// Asignar nombre y OpenAPI
.WithName("RegisterUser")
.WithOpenApi();


// Ruta de login
app.MapPost("/auth/login", async (
    LoginRequest request,
    UsuariosRepository usuariosRepo,
    SesionesUsuariosRepository sesionesRepo,
    JwtGenerator jwtGenerator,
    HttpContext httpContext
) =>
{
    // Validaciones mínimas
    if (string.IsNullOrWhiteSpace(request.Email) ||
        string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new
        {
            message = "El email y la contraseña son obligatorios."
        });
    }

    // Buscar al usuario (solo activos)
    var usuario = await usuariosRepo.ObtenerUsuarioPorEmailAsync(request.Email);

    // Mensaje genérico para no filtrar información
    if (usuario == null)
    {
        return Results.BadRequest(new
        {
            message = "No es posible iniciar sesión con las credenciales proporcionadas."
        });
    }

    // Comparar contraseñas
    bool passwordOk = PasswordHasher.VerifyPassword(request.Password, usuario.PasswordHash);

    if (!passwordOk)
    {
        return Results.BadRequest(new
        {
            message = "No es posible iniciar sesión con las credenciales proporcionadas."
        });
    }

    // Verificar email
    if (usuario.EmailVerificado == 0)
    {
        return Results.BadRequest(new
        {
            message = "Debes verificar tu email antes de iniciar sesión."
        });
    }

    // === Generar tokens ===

    // Crear refresh-token en texto plano (el repositorio lo hashea internamente)
    var refreshToken = TokenGenerator.GenerateToken(64);
    var refreshExpiraEn = DateTime.UtcNow.AddDays(7);

    // Datos de auditoría
    var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
    var ip = httpContext.Connection.RemoteIpAddress?.ToString();

    // Crear la sesión en BD y obtener el ID de sesión generado
    var idSesion = await sesionesRepo.CrearSesionAsync(
        usuario.IdUsuario,
        refreshToken,
        refreshExpiraEn,
        userAgent,
        ip
    );

    // Generar access token incluyendo el ID de sesión
    var accessToken = jwtGenerator.GenerateJwt(usuario, idSesion);

    // Limitar a máximo 4 sesiones activas
    await sesionesRepo.LimitarSesionesActivasAsync(usuario.IdUsuario, 4);

    // Respuesta
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
            accessTokenExpiresInMinutes = 15,  // cuando definamos el JWT real
            refreshToken,
            refreshTokenExpiresAt = refreshExpiraEn
        }
    });
})
.WithName("LoginUser")
.WithOpenApi();


// Ruta de verificación de email
app.MapGet("/auth/verify-email/{token}", async (
    string token,
    VerificacionEmailRepository verifRepo,
    UsuariosRepository usuariosRepo
) =>
{
    // Buscar el token
    var data = await verifRepo.ObtenerTokenAsync(token);

    if (data is null)
    {
        return Results.BadRequest(new
        {
            message = "Token inválido o ya utilizado."
        });
    }

    var (idUsuario, expiraEn) = data.Value;

    // Validar expiración
    if (expiraEn < DateTime.UtcNow)
    {
        return Results.BadRequest(new
        {
            message = "El token ha expirado. Solicita uno nuevo."
        });
    }

    // Marcar email como verificado
    await usuariosRepo.VerificarEmailAsync(idUsuario);

    // Invalidar token
    await verifRepo.InvalidarTokenAsync(token);

    return Results.Ok(new
    {
        message = "Email verificado correctamente. Ya puedes iniciar sesión."
    });
})
.WithName("VerifyEmail")
.WithOpenApi();


// Ruta de solicitud de reseteo de contraseña
app.MapPost("/auth/forgot-password", async (
    ForgotPasswordRequest request,
    UsuariosRepository usuariosRepo,
    ResetPasswordRepository resetRepo
) =>
{
    // Validación del email
    if (string.IsNullOrWhiteSpace(request.Email))
    {
        return Results.BadRequest(new
        {
            message = "El email es obligatorio."
        });
    }

    // Intentar obtener al usuario (solo usuarios activos)
    var usuario = await usuariosRepo.ObtenerUsuarioPorEmailAsync(request.Email);

    // Seguridad: siempre respondemos lo mismo, exista o no
    if (usuario == null)
    {
        return Results.Ok(new
        {
            message = "Si el correo está registrado, recibirás instrucciones para recuperar tu contraseña."
        });
    }

    // Generar token seguro (64 caracteres hex)
    var token = TokenGenerator.GenerateToken(32);
    var expiraEn = DateTime.UtcNow.AddHours(1);

    // Guardar token en la tabla RESET_PASSWORD
    await resetRepo.CrearTokenResetAsync(usuario.IdUsuario, token, expiraEn);

    // Construir URL (por ahora solo se devuelve)
    var baseUrl = "https://localhost:5001";
    var resetLink = $"{baseUrl}/auth/reset-password/{token}";

    // En producción esto se enviaría por correo
    return Results.Ok(new
    {
        message = "Si el correo está registrado, recibirás instrucciones para recuperar tu contraseña.",
        reset_url_demo = resetLink
    });
})
.WithName("ForgotPassword")
.WithOpenApi();


// Ruta de reseteo de contraseña
app.MapPost("/auth/reset-password", async (
    ResetPasswordRequest request,
    ResetPasswordRepository resetRepo,
    UsuariosRepository usuariosRepo
) =>
{
    // Validación mínima
    if (string.IsNullOrWhiteSpace(request.Token) ||
        string.IsNullOrWhiteSpace(request.NewPassword))
    {
        return Results.BadRequest(new
        {
            message = "El token y la nueva contraseña son obligatorios."
        });
    }

    // Reglas mínimas de seguridad
    var (isValid, error) = PasswordPolicy.Validate(request.NewPassword);

    if (!isValid)
        return Results.BadRequest(new { message = error });

    // Buscar token
    var tokenInfo = await resetRepo.ObtenerTokenValidoAsync(request.Token);

    if (tokenInfo == null)
    {
        // Mensaje genérico para evitar filtración de información
        return Results.BadRequest(new
        {
            message = "El token es inválido o ya no está disponible."
        });
    }

    // Validar expiración
    if (tokenInfo.ExpiraEn < DateTime.UtcNow)
    {
        return Results.BadRequest(new
        {
            message = "El token de recuperación ha expirado."
        });
    }

    // Validar si ya fue usado
    if (tokenInfo.Estado == 0)
    {
        return Results.BadRequest(new
        {
            message = "El token ya fue utilizado anteriormente."
        });
    }

    // Encriptar nueva contraseña
    var newHash = PasswordHasher.HashPassword(request.NewPassword);

    // Actualizar contraseña del usuario
    await usuariosRepo.ActualizarPasswordAsync(tokenInfo.IdUsuario, newHash);

    // Marcar token como usado
    await resetRepo.MarcarTokenComoUsadoAsync(tokenInfo.IdReset);

    return Results.Ok(new
    {
        message = "Tu contraseña ha sido actualizada correctamente."
    });
})
.WithName("ResetPassword")
.WithOpenApi();


// Ruta de refresh token
app.MapPost("/auth/refresh-token", async (
    RefreshTokenRequest request,
    SesionesUsuariosRepository sesionesRepo,
    UsuariosRepository usuariosRepo,
    JwtGenerator jwtGenerator
) =>
{
    // 1. Validar entrada
    if (string.IsNullOrWhiteSpace(request.RefreshToken))
    {
        return Results.BadRequest(new
        {
            message = "El refresh token es obligatorio."
        });
    }

    // 2. Hashear el token recibido (porque en BD guardamos el hash)
    var hash = TokenGenerator.HashToken(request.RefreshToken);

    // 3. Buscar sesión activa en la BD mediante el hash
    var sesion = await sesionesRepo.ObtenerSesionActivaPorHashAsync(hash);

    if (sesion == null)
    {
        // Mensaje genérico por seguridad
        return Results.BadRequest(new
        {
            message = "El token ya no es válido."
        });
    }

    // 4. Validar expiración
    if (sesion.ExpiraEn < DateTime.UtcNow)
    {
        await sesionesRepo.InvalidarSesionPorHashAsync(hash);

        return Results.BadRequest(new
        {
            message = "El refresh token ha expirado."
        });
    }

    // Validar estado de la sesión
    if (sesion.Estado != 1)
        return Results.BadRequest(new { message = "Sesión inválida." });


    // 5. Obtener al usuario asociado a la sesión
    var usuario = await usuariosRepo.ObtenerUsuarioPorIdAsync(sesion.IdUsuario);

    if (usuario == null || usuario.Estado == 0)
    {
        return Results.BadRequest(new
        {
            message = "El usuario asociado ya no está disponible."
        });
    }

    // 6. Invalidar la sesión actual (rotación segura)
    await sesionesRepo.InvalidarSesionPorHashAsync(hash);

    // 7. Generar nuevos tokens (el repositorio hashea internamente)
    var nuevoRefreshToken = TokenGenerator.GenerateToken(64);
    var expiraEn = DateTime.UtcNow.AddDays(7);

    // Crear nueva sesión y obtener ID
    var nuevoIdSesion = await sesionesRepo.CrearSesionAsync(
        usuario.IdUsuario,
        nuevoRefreshToken,
        expiraEn,
        null,
        null
    );

    // Generar access token con la nueva sesión
    var nuevoAccessToken = jwtGenerator.GenerateJwt(usuario, nuevoIdSesion);

    // 9. Devolver respuesta
    return Results.Ok(new
    {
        access_token = nuevoAccessToken,
        refresh_token = nuevoRefreshToken,   // ← este va al cliente
        expires_in = 15 * 60,
        token_type = "Bearer"
    });
})
.WithName("RefreshTokenSecure")
.WithOpenApi();

// Ruta de cambio de contraseña (requiere JWT)
app.MapPost("/auth/change-password", async (
    ChangePasswordRequest request,
    HttpContext http,
    UsuariosRepository usuariosRepo,
    SesionesUsuariosRepository sesionesRepo
) =>
{
    // Obtener ID del usuario desde JWT
    var idUsuario = long.Parse(
        http.User.Claims.First(c => c.Type == "id").Value
    );

    // Obtener datos
    var usuario = await usuariosRepo.ObtenerUsuarioPorIdAsync(idUsuario);

    if (usuario == null)
    {
        return Results.BadRequest(new { message = "El usuario no está disponible." });
    }

    // Verificar contraseña actual
    if (!PasswordHasher.VerifyPassword(request.PasswordActual, usuario.PasswordHash))
    {
        return Results.BadRequest(new { message = "La contraseña actual es incorrecta." });
    }

    // Evitar que use la misma contraseña
    if (PasswordHasher.VerifyPassword(request.PasswordNueva, usuario.PasswordHash))
    {
        return Results.BadRequest(new { message = "La nueva contraseña no puede ser igual a la anterior." });
    }

    // Reglas mínimas de seguridad
    var (isValid, error) = PasswordPolicy.Validate(request.PasswordNueva);

    if (!isValid)
        return Results.BadRequest(new { message = error });

    // Hashear nueva contraseña
    var hashNuevo = PasswordHasher.HashPassword(request.PasswordNueva);

    // Actualizar contraseña
    await usuariosRepo.ActualizarPasswordAsync(usuario.IdUsuario, hashNuevo);

    // Invalida todas las sesiones activas (por seguridad)
    await sesionesRepo.InvalidarTodasPorUsuarioAsync(usuario.IdUsuario);

    return Results.Ok(new 
    { 
        message = "Tu contraseña ha sido actualizada. Por seguridad, vuelve a iniciar sesión." 
    });
})
.RequireAuthorization()
.WithName("ChangePassword")
.WithOpenApi();

// Ruta de logout
app.MapPost("/auth/logout", async (
    HttpContext http,
    SesionesUsuariosRepository sesionesRepo
) =>
{
    if (!http.User.Identity!.IsAuthenticated)
        return Results.Unauthorized();

    var idSesion = long.Parse(http.User.Claims.First(c => c.Type == "id_sesion").Value);
    var idUsuario = long.Parse(http.User.Claims.First(c => c.Type == "id").Value);

    var exito = await sesionesRepo.InvalidarSesionPorIdAsync(idSesion);

    return Results.Ok(new
    {
        message = exito ? "Sesión cerrada correctamente." :
                        "La sesión ya estaba cerrada o no existe."
    });
})
.RequireAuthorization()
.WithOpenApi();

// Ruta de logout de todas las sesiones
app.MapPost("/auth/logout-all", async (
    HttpContext http,
    SesionesUsuariosRepository sesionesRepo
) =>
{
    if (!http.User.Identity!.IsAuthenticated)
        return Results.Unauthorized();

    var idUsuario = long.Parse(http.User.Claims.First(c => c.Type == "id").Value);

    var cantidad = await sesionesRepo.InvalidarTodasPorUsuarioAsync(idUsuario);

    return Results.Ok(new
    {
        message = $"Todas las sesiones han sido cerradas ({cantidad})."
    });
})
.RequireAuthorization()
.WithOpenApi();

// Ruta para obtener las sesiones activas del usuario
app.MapGet("/auth/sessions", async (
    HttpContext http,
    SesionesUsuariosRepository sesionesRepo
) =>
{
    if (!http.User.Identity!.IsAuthenticated)
        return Results.Unauthorized();

    var idUsuario = long.Parse(http.User.Claims.First(c => c.Type == "id").Value);

    var sesiones = await sesionesRepo.ObtenerSesionesActivasPorUsuarioAsync(idUsuario);

    return Results.Ok(new
    {
        sesiones = sesiones.Select(s => new {
            s.IdSesion,
            s.UserAgent,
            s.IpOrigen,
            s.Creacion,
            s.ExpiraEn
        })
    });
})
.RequireAuthorization()
.WithOpenApi();

// Ruta para revocar una sesión específica
app.MapPost("/auth/sessions/revoke/{idSesion:long}", async (
    long idSesion,
    HttpContext http,
    SesionesUsuariosRepository sesionesRepo
) =>
{
    if (!http.User.Identity!.IsAuthenticated)
        return Results.Unauthorized();

    var idUsuario = long.Parse(http.User.Claims.First(c => c.Type == "id").Value);

    // Validar que la sesión pertenezca al usuario
    var sesion = await sesionesRepo.ObtenerSesionPorIdAsync(idSesion);

    if (sesion == null || sesion.IdUsuario != idUsuario)
        return Results.BadRequest(new { message = "No puedes revocar esta sesión." });

    await sesionesRepo.InvalidarSesionPorIdAsync(idSesion);

    return Results.Ok(new { message = "Sesión revocada correctamente." });
})
.RequireAuthorization()
.WithOpenApi();

// Ejecutar la app
app.Urls.Add("http://0.0.0.0:8080");
app.Run();
