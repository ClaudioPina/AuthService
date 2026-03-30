# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Comandos principales

```bash
# Build
dotnet build

# Ejecutar en desarrollo (HTTP: localhost:5091, HTTPS: localhost:7075)
dotnet run --project AuthService.Api

# Tests unitarios (no requieren Docker)
dotnet test --filter "FullyQualifiedName~Unit"

# Tests de integración (requiere Docker corriendo para Testcontainers)
dotnet test --filter "FullyQualifiedName~Integration"

# Todos los tests
dotnet test

# Build Docker
docker build -t authservice .
docker run -p 8080:8080 authservice
```

Swagger disponible en `/swagger` solo en entorno Development.

## Arquitectura

**AuthService** es un microservicio de autenticación independiente en .NET 8 Minimal APIs. No usa Controllers.

### Capas

```
AuthEndpoints.cs (rutas + extracción de claims)
    └── IAutenticacionService / AutenticacionService (lógica de negocio)
         ├── IEmailService / EmailService (envío de emails via Resend SDK)
         └── Repositories (acceso a datos via Npgsql raw SQL)
              └── AppDbContext (Npgsql + Polly retry, 3 intentos, 300ms)
```

### Modelo de autenticación híbrido

- **Stateless**: JWT Access Token (15 min por defecto, configurable) enviado como Bearer header. Claims: `id`, `email`, `id_sesion`.
- **Stateful**: Refresh Token hasheado (SHA-256) almacenado en BD + sesión persistida. Permite revocación.
- **Session validation**: `ValidarSesionMiddleware` intercepta todas las rutas protegidas y verifica el claim `id_sesion` del JWT contra la tabla `SESIONES_USUARIOS` en PostgreSQL.

### Carpetas clave

| Carpeta | Propósito |
|---------|-----------|
| `Data/` | `AppDbContext` — manejo de conexiones con retry via Polly |
| `Models/` | Entidades: `Usuario`, `SesionUsuario`, `RefreshToken`, `ResetPasswordToken` |
| `DTOs/Auth/` | DTOs de request para cada endpoint |
| `Repositories/` | Acceso a datos (queries SQL directas con Npgsql) |
| `Services/` | `IAutenticacionService` + `AutenticacionService` (lógica de negocio), `IEmailService` + `EmailService` (Resend SDK) |
| `Endpoints/` | `AuthEndpoints.cs` — extension method `MapAuthEndpoints()` con las 11 rutas |
| `Configuration/` | `SwaggerConfig.cs` — extension methods para Swagger con JWT Bearer |
| `Middlewares/` | `ValidarSesionMiddleware` — valida sesión activa en DB |
| `Utils/` | `JwtGenerator`, `PasswordHasher` (BCrypt), `PasswordPolicy`, `TokenGenerator` (SHA-256) |

## Base de datos

**PostgreSQL** — sin Entity Framework. El esquema se gestiona con `script_DB.sql`.

Tablas principales:
- `USUARIOS` — cuentas de usuario con BCrypt hash
- `SESIONES_USUARIOS` — sesiones activas con refresh token hasheado, IP y user-agent
- `VERIFICACION_EMAIL` — tokens de verificación (TTL configurable, default 24h)
- `RESET_PASSWORD` — tokens de reset (TTL configurable, default 1h)

**Al modificar el esquema**: actualizar `script_DB.sql` y los repositories correspondientes. No hay migraciones automáticas.

Convención SQL del proyecto: parámetros con `@param` (Npgsql), nombres de tablas y columnas en `UPPER_SNAKE_CASE`.

### Conexión a la base de datos

`AppDbContext` resuelve la cadena de conexión en este orden:
1. Variable de entorno `DATABASE_URL` (formato PostgreSQL URL — usado por Fly.io)
2. `ConnectionStrings:PostgresDb` de `appsettings.json`

## Dependencias NuGet relevantes

**AuthService.Api**
- `BCrypt.Net-Next` — hashing de passwords
- `Npgsql` — driver PostgreSQL (sin EF Core)
- `System.IdentityModel.Tokens.Jwt` + `Microsoft.AspNetCore.Authentication.JwtBearer` — JWT
- `Polly` — retry policy para conexiones DB (maneja `NpgsqlException`)
- `Swashbuckle.AspNetCore` — Swagger/OpenAPI
- `Resend` (v0.2.2) — envío de emails transaccionales
- `Serilog.AspNetCore` + `Serilog.Sinks.Console` — logging estructurado

**AuthService.Tests**
- `xUnit` — framework de testing
- `FluentAssertions` (6.12.1) — assertions legibles
- `Microsoft.AspNetCore.Mvc.Testing` — `WebApplicationFactory<Program>` para integration tests
- `Testcontainers.PostgreSql` (3.10.0) — PostgreSQL real en Docker para integration tests

## Tests

El proyecto `AuthService.Tests` tiene dos categorías:

**Unit** (`AuthService.Tests/Unit/`)
- `PasswordPolicyTests.cs` — 7 tests sobre validación de política de contraseñas
- `PasswordHasherTests.cs` — 4 tests sobre BCrypt hashing
- `TokenGeneratorTests.cs` — 5 tests sobre generación y hash de tokens
- `JwtGeneratorTests.cs` — 3 tests sobre generación y validación de JWTs

**Integration** (`AuthService.Tests/Integration/`)
- `AuthWebAppFactory.cs` — levanta la app real con PostgreSQL en Docker (Testcontainers) y reemplaza `IEmailService` con `FakeEmailService`
- `FakeEmailService.cs` — implementación fake con `ConcurrentBag` para capturar emails enviados en tests
- `AuthIntegrationTests.cs` — 16 tests de extremo a extremo sobre todos los endpoints

Los integration tests requieren Docker corriendo. Usan `IClassFixture<AuthWebAppFactory>` (una sola instancia del contenedor por clase) e `IAsyncLifetime` para setup/teardown asíncrono.

Para que `WebApplicationFactory` funcione, `Program.cs` termina con `public partial class Program { }`.

## Endpoints

| Método | Ruta | Autenticación | Rate Limit |
|--------|------|---------------|------------|
| POST | `/auth/register` | Público | 5 req/min por IP |
| POST | `/auth/login` | Público | 10 req/min por IP |
| GET | `/auth/verify-email/{token}` | Público | — |
| POST | `/auth/forgot-password` | Público | 3 req/min por IP |
| POST | `/auth/reset-password` | Público | — |
| POST | `/auth/refresh-token` | Público | — |
| POST | `/auth/change-password` | JWT requerido | — |
| POST | `/auth/logout` | JWT requerido | — |
| POST | `/auth/logout-all` | JWT requerido | — |
| GET | `/auth/sessions` | JWT requerido | — |
| POST | `/auth/sessions/revoke/{idSesion}` | JWT requerido | — |

El rate limiting es por IP (`RateLimitPartition.GetFixedWindowLimiter`), no global.

## Seguridad implementada

- **Refresh token rotation**: cada refresh invalida la sesión anterior y crea una nueva
- **Logout forzado al cambiar contraseña**: `ChangePasswordAsync` invalida TODAS las sesiones
- **Prevención de enumeración de usuarios**: `ForgotPasswordAsync` siempre retorna la misma respuesta
- **Tokens hasheados**: refresh tokens y tokens de email/reset se almacenan como SHA-256, nunca en texto plano
- **Docker no-root**: el contenedor corre con usuario `appuser` sin privilegios
- **CORS configurable**: en Development acepta cualquier origen; en producción lee `Cors:AllowedOrigins`

## Configuración esperada (appsettings.json)

El archivo no está en el repo (excluido por `.gitignore`). Usar `appsettings.example.json` como base.

Secciones requeridas:

```json
{
  "Jwt": {
    "Key": "<clave de mínimo 32 caracteres>",
    "Issuer": "AuthService",
    "Audience": "AuthServiceClients",
    "AccessTokenExpirationMinutes": 15
  },
  "Tokens": {
    "RefreshTokenExpirationDays": 7,
    "EmailVerificationExpirationHours": 24,
    "PasswordResetExpirationHours": 1
  },
  "App": {
    "BaseUrl": "https://tu-app.fly.dev"
  },
  "Email": {
    "ResendApiKey": "re_...",
    "FromAddress": "noreply@tudominio.com",
    "FromName": "AuthService"
  },
  "Cors": {
    "AllowedOrigins": ["https://tu-frontend.com"]
  },
  "ConnectionStrings": {
    "PostgresDb": "Host=...;Database=...;Username=...;Password=..."
  },
  "Serilog": {
    "MinimumLevel": { "Default": "Information" },
    "WriteTo": [{ "Name": "Console" }]
  }
}
```

## CI/CD

**GitHub Actions** — dos workflows en `.github/workflows/`:

- `ci.yml`: se ejecuta en push/PR a `main`. Pasos: restore → build Release → test.
- `deploy.yml`: se ejecuta solo cuando `ci.yml` completa con éxito. Despliega a Fly.io con `flyctl deploy --remote-only`.

**Secreto requerido en GitHub**: `FLY_API_TOKEN` (generar con `fly tokens create deploy`).

El proyecto está desplegado en **Fly.io** (`fly.toml`, región `gru`, puerto 8080).

## Notas de contexto

- `script_DB.sql` contiene tablas residuales de negocio (`TRANSACCIONES`, `ORGANIZACION`) que no pertenecen al microservicio de auth — ignorarlas.
- Hay columnas de multi-tenancy (`propietario`) que son remanentes de la arquitectura anterior.
- En entorno Development, `RegisterAsync` y `ForgotPasswordAsync` retornan la URL del token directamente en la respuesta (`verificar_url_dev`, `reset_url_dev`) para facilitar el testing sin email real.
