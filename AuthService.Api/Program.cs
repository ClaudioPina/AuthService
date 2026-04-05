using System.Text;
using System.Threading.RateLimiting;
using AuthService.Api.Configuration;
using AuthService.Api.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Serilog.Context;
using AuthService.Api.Data;
using AuthService.Api.Endpoints;
using AuthService.Api.Middlewares;
using AuthService.Api.Repositories;
using AuthService.Api.Services;
using AuthService.Api.Utils;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
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

    options.AddPolicy("resendverification-policy", ctx =>
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

// ── OpenTelemetry Metrics ─────────────────────────────────────────────────────
// Instrumenta métricas de ASP.NET Core y las del Meter "AuthService" (AuthMetrics.cs).
// Las expone en /metrics en formato Prometheus para ser consumidas por Grafana u otras
// herramientas de monitoreo.
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics =>
    {
        metrics.AddAspNetCoreInstrumentation(); // request count, duration, errors por ruta
        metrics.AddMeter("AuthService");        // contadores de negocio (logins, registros, etc.)
        metrics.AddPrometheusExporter();
    });

// Singleton porque IMeterFactory (que usa AuthMetrics internamente) es thread-safe.
builder.Services.AddSingleton<AuthMetrics>();

// ── Redis / Distributed Cache ─────────────────────────────────────────────────
// Usado por ValidarSesionMiddleware para cachear sesiones activas (TTL 5 min)
// y reducir queries a BD por request.
// Si Redis:ConnectionString está vacío, se usa MemoryCache como fallback
// (funciona correctamente en despliegues de instancia única como Fly.io).
var redisConn = builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConn))
    builder.Services.AddStackExchangeRedisCache(o => o.Configuration = redisConn);
else
    builder.Services.AddDistributedMemoryCache();

// ── Health Checks ──────────────────────────────────────────────────────────────
// Implementaciones custom sin paquetes externos: evita dependencias innecesarias.
// GET /health retorna JSON con estado de cada dependencia.
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres")
    .AddCheck<RedisHealthCheck>("redis");

// ── Inyección de dependencias ──────────────────────────────────────────────────
// Scoped: se crea una instancia nueva por cada request HTTP.
builder.Services.AddScoped<AppDbContext>();
builder.Services.AddScoped<UsuariosRepository>();
builder.Services.AddScoped<VerificacionEmailRepository>();
builder.Services.AddScoped<ResetPasswordRepository>();
builder.Services.AddScoped<SesionesUsuariosRepository>();
builder.Services.AddScoped<IntentosLoginRepository>();
builder.Services.AddScoped<AuditoriaRepository>();
builder.Services.AddScoped<IAutenticacionService, AutenticacionService>();

// Seleccionar proveedor de email según configuración.
// Email:Provider = "Resend" (producción) o "Smtp" (desarrollo local con MailHog/Mailtrap).
var emailProvider = builder.Configuration["Email:Provider"] ?? "Resend";
if (emailProvider.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddScoped<IEmailService, SmtpEmailService>();
else
    builder.Services.AddScoped<IEmailService, EmailService>();

// Singleton: una sola instancia compartida para toda la vida de la app.
builder.Services.AddSingleton<JwtGenerator>();

// Background service: limpia tokens y sesiones expirados cada hora.
builder.Services.AddHostedService<CleanupExpiredTokensService>();

// HIBP: HttpClient tipado con User-Agent requerido por la API pública
builder.Services.AddHttpClient<IHibpService, HibpService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("AuthService/1.0");
    client.Timeout = TimeSpan.FromSeconds(3); // fail fast si HIBP no responde
});

// Resend SDK: requiere HttpClient y las opciones de API key
builder.Services.AddOptions();
builder.Services.AddHttpClient<ResendClient>();
builder.Services.Configure<ResendClientOptions>(o =>
    o.ApiToken = builder.Configuration["Email:ResendApiKey"]!);
builder.Services.AddTransient<IResend, ResendClient>();

// ── Build ──────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Validación de configuración crítica ───────────────────────────────────────
// Si falta algún valor obligatorio la app se detiene inmediatamente con un mensaje
// claro. Mejor fallar rápido al arrancar que fallar tarde con un error críptico
// en mitad de un request.
{
    var cfg    = app.Configuration;
    var errores = new List<string>();

    var jwtKey = cfg["Jwt:Key"];
    if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
        errores.Add("Jwt:Key debe tener al menos 32 caracteres.");

    if (string.IsNullOrWhiteSpace(cfg["App:BaseUrl"]))
        errores.Add("App:BaseUrl es obligatorio (se usa en los links de email).");

    if (string.IsNullOrWhiteSpace(cfg["Email:FromAddress"]))
        errores.Add("Email:FromAddress es obligatorio.");

    // Validación condicional según el proveedor de email configurado.
    var emailProviderCfg = cfg["Email:Provider"] ?? "Resend";
    if (emailProviderCfg.Equals("Resend", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(cfg["Email:ResendApiKey"]))
            errores.Add("Email:ResendApiKey es obligatorio cuando Email:Provider es 'Resend'.");
    }
    else if (emailProviderCfg.Equals("Smtp", StringComparison.OrdinalIgnoreCase))
    {
        if (string.IsNullOrWhiteSpace(cfg["Email:Smtp:Host"]))
            errores.Add("Email:Smtp:Host es obligatorio cuando Email:Provider es 'Smtp'.");
        if (string.IsNullOrWhiteSpace(cfg["Email:Smtp:Port"]))
            errores.Add("Email:Smtp:Port es obligatorio cuando Email:Provider es 'Smtp'.");
    }

    // Google:ClientId solo es obligatorio si está definido en config (habilita el login con Google).
    // Si no está presente, el endpoint /auth/google retornará error en runtime pero no impide el arranque.
    if (cfg["Google:ClientId"] is not null && string.IsNullOrWhiteSpace(cfg["Google:ClientId"]))
        errores.Add("Google:ClientId está definido pero vacío. Asigna un valor válido o elimina la clave.");

    var dbConn = Environment.GetEnvironmentVariable("DATABASE_URL")
                 ?? cfg.GetConnectionString("PostgresDb");
    if (string.IsNullOrWhiteSpace(dbConn))
        errores.Add("Se requiere DATABASE_URL (env) o ConnectionStrings:PostgresDb (appsettings).");

    if (errores.Count > 0)
    {
        foreach (var e in errores)
            Log.Fatal("Configuración inválida: {Error}", e);
        Log.CloseAndFlush();
        Environment.Exit(1);
    }
}

// ── Pipeline de middleware (el orden importa) ──────────────────────────────────

// Correlation ID: debe ir primero para que TODOS los logs del request lo incluyan.
// Si el cliente envía X-Correlation-Id (ej. frontend), se propaga; si no, se genera uno nuevo.
app.Use(async (ctx, next) =>
{
    var correlationId = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault()
                        ?? Guid.NewGuid().ToString("N");
    ctx.Response.Headers["X-Correlation-Id"] = correlationId;
    using (LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});

// Security headers: previenen clickjacking y MIME sniffing en clientes web.
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"]        = "DENY";
    ctx.Response.Headers["Referrer-Policy"]        = "no-referrer";
    await next();
});

app.UseSerilogRequestLogging(); // log de cada request HTTP
app.UseSwaggerInDevelopment();  // Swagger solo en Development
app.UseExceptionHandler(errorApp =>
    errorApp.Run(async ctx =>
    {
        var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        ctx.Response.StatusCode = ex is UnauthorizedAccessException
            ? StatusCodes.Status401Unauthorized
            : StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/json";

        // En producción se devuelve un mensaje genérico para no filtrar detalles
        // internos (stack traces, nombres de tablas, mensajes de Npgsql, etc.).
        // En Development se expone el mensaje real para facilitar el debug local.
        var message = app.Environment.IsDevelopment()
            ? ex?.Message ?? "Error interno del servidor."
            : "Ocurrió un error inesperado. Por favor, intenta nuevamente más tarde.";

        await ctx.Response.WriteAsJsonAsync(new { message });
    }));
// Procesar X-Forwarded-For y X-Forwarded-Proto desde el proxy de Fly.io.
// Sin esto, RemoteIpAddress contiene la IP del proxy y el rate limiting y la
// auditoría operan sobre una IP incorrecta.
// En producción se limpian KnownNetworks/KnownProxies para confiar en el proxy
// de Fly.io, que siempre reemplaza X-Forwarded-For con la IP real del cliente.
// En desarrollo no hay proxy, así que se usa la configuración por defecto (loopback).
var forwardedOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
};
if (!app.Environment.IsDevelopment())
{
    forwardedOptions.KnownNetworks.Clear();
    forwardedOptions.KnownProxies.Clear();
}
app.UseForwardedHeaders(forwardedOptions);
app.UseHttpsRedirection();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ValidarSesionMiddleware>();

// ── Endpoints ─────────────────────────────────────────────────────────────────

// /health con respuesta detallada: muestra el estado de cada dependencia (postgres, redis).
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        var result = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name        = e.Key,
                status      = e.Value.Status.ToString(),
                description = e.Value.Description
            })
        };
        await ctx.Response.WriteAsJsonAsync(result);
    }
});

// /metrics con protección opcional por API key.
// Si Metrics:ApiKey está definido en config, exige header X-Metrics-Token.
// Si no está definido, el endpoint es público (útil en desarrollo local).
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Path == "/metrics")
    {
        var apiKey = app.Configuration["Metrics:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey) &&
            (!ctx.Request.Headers.TryGetValue("X-Metrics-Token", out var token) || token != apiKey))
        {
            ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await ctx.Response.WriteAsync("Unauthorized");
            return;
        }
    }
    await next();
});
app.MapPrometheusScrapingEndpoint();

app.MapAuthEndpoints();                      // definidos en Endpoints/AuthEndpoints.cs

// En producción (Docker/Fly.io) escucha en 0.0.0.0:8080
if (!app.Environment.IsDevelopment())
    app.Urls.Add("http://0.0.0.0:8080");

app.Run();

// Expone la clase Program para que WebApplicationFactory pueda usarla en tests
public partial class Program { }
