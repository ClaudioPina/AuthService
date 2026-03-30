using System.Text;
using System.Threading.RateLimiting;
using AuthService.Api.Configuration;
using AuthService.Api.Data;
using AuthService.Api.Endpoints;
using AuthService.Api.Middlewares;
using AuthService.Api.Repositories;
using AuthService.Api.Services;
using AuthService.Api.Utils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Resend;
using Serilog;

// ──────────────────────────────────────────────────────────────────────────────
// Configuración inicial de Serilog (antes del builder para capturar errores de
// arranque). En producción solo loguea Information+; en dev loguea todo.
// ──────────────────────────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

// Serilog reemplaza el sistema de logging por defecto de .NET
builder.Host.UseSerilog((ctx, services, config) =>
    config.ReadFrom.Configuration(ctx.Configuration)
          .ReadFrom.Services(services)
          .Enrich.FromLogContext());

// ── Swagger con soporte JWT (definido en Configuration/SwaggerConfig.cs) ──────
builder.Services.AddSwaggerWithJwt();
builder.Services.AddAuthorization();

// ── Autenticación JWT ──────────────────────────────────────────────────────────
// La lectura de Jwt:Key se hace DENTRO del lambda porque AddJwtBearer registra
// una acción de tipo Configure<JwtBearerOptions>, que se ejecuta de forma diferida
// al resolver las opciones (no durante el setup del builder). Esto permite que
// WebApplicationFactory inyecte su configuración de test antes de que se lea la clave.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey = Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"]!);
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(jwtKey)
        };
    });

// ── CORS: en dev permite todo, en prod solo orígenes configurados ──────────────
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
    {
        if (builder.Environment.IsDevelopment())
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        else
            policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
    }));

// ── Rate Limiting: protege endpoints sensibles contra ataques de fuerza bruta ──
// Se particiona por IP: cada dirección IP tiene su propio contador independiente.
// Así un atacante no puede consumir el límite de otros usuarios legítimos.
builder.Services.AddRateLimiter(options =>
{
    // 10 intentos de login por IP por minuto
    options.AddPolicy("login-policy", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0
            }));

    // 5 registros por IP por minuto
    options.AddPolicy("register-policy", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0
            }));

    // 3 solicitudes de recuperación de contraseña por IP por minuto
    options.AddPolicy("forgotpassword-policy", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window      = TimeSpan.FromMinutes(1),
                QueueLimit  = 0
            }));

    // 429 Too Many Requests cuando se supera el límite
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── Health Checks ──────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks();

// ── Inyección de dependencias ──────────────────────────────────────────────────
// Scoped: se crea una instancia nueva por cada request HTTP.
builder.Services.AddScoped<AppDbContext>();
builder.Services.AddScoped<UsuariosRepository>();
builder.Services.AddScoped<VerificacionEmailRepository>();
builder.Services.AddScoped<ResetPasswordRepository>();
builder.Services.AddScoped<SesionesUsuariosRepository>();
builder.Services.AddScoped<IAutenticacionService, AutenticacionService>();
builder.Services.AddScoped<IEmailService, EmailService>();

// Singleton: una sola instancia compartida para toda la vida de la app.
builder.Services.AddSingleton<JwtGenerator>();

// Resend SDK: requiere HttpClient y las opciones de API key
builder.Services.AddOptions();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(o =>
    o.ApiToken = builder.Configuration["Email:ResendApiKey"]!);
builder.Services.AddTransient<IResend, ResendClient>();

// ── Build ──────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Pipeline de middleware (el orden importa) ──────────────────────────────────
app.UseSerilogRequestLogging(); // log de cada request HTTP
app.UseSwaggerInDevelopment();  // Swagger solo en Development
app.UseExceptionHandler(errorApp =>
    errorApp.Run(async ctx =>
    {
        ctx.Response.StatusCode  = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/json";
        var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        if (ex is UnauthorizedAccessException)
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await ctx.Response.WriteAsJsonAsync(new { message = ex?.Message ?? "Error interno del servidor." });
    }));
app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ValidarSesionMiddleware>();

// ── Endpoints ─────────────────────────────────────────────────────────────────
app.MapHealthChecks("/health"); // GET /health → {"status":"Healthy"}
app.MapAuthEndpoints();        // definidos en Endpoints/AuthEndpoints.cs

app.Urls.Add("http://0.0.0.0:8080");

app.Run();

// Expone la clase Program para que WebApplicationFactory pueda usarla en tests
public partial class Program { }
