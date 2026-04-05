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
| `Models/` | Entidades: `Usuario`, `SesionUsuario`, `ResetPasswordToken`, `IntentoLogin` |
| `DTOs/Auth/` | DTOs de request para cada endpoint |
| `Repositories/` | Acceso a datos (queries SQL directas con Npgsql): `UsuariosRepository`, `SesionesUsuariosRepository`, `VerificacionEmailRepository`, `ResetPasswordRepository`, `IntentosLoginRepository`, `AuditoriaRepository` |
| `Services/` | `IAutenticacionService` + `AutenticacionService` (lógica de negocio), `IEmailService` + `EmailService` (Resend) + `SmtpEmailService` (SMTP local), `IHibpService` + `HibpService` (HaveIBeenPwned k-anonymity), `CleanupExpiredTokensService` (BackgroundService), `AuthMetrics` (contadores OpenTelemetry) |
| `Endpoints/` | `AuthEndpoints.cs` — extension method `MapAuthEndpoints()` con las 15 rutas |
| `Configuration/` | `SwaggerConfig.cs` — extension methods para Swagger con JWT Bearer |
| `Middlewares/` | `ValidarSesionMiddleware` — valida sesión activa en DB |
| `HealthChecks/` | `PostgresHealthCheck.cs`, `RedisHealthCheck.cs` — health checks expuestos en `GET /health` |
| `Utils/` | `JwtGenerator`, `PasswordHasher` (BCrypt), `PasswordPolicy`, `TokenGenerator` (SHA-256) |

## Base de datos

**PostgreSQL** — sin Entity Framework. El esquema se gestiona con `script_DB.sql`.

Tablas principales:
- `USUARIOS` — cuentas de usuario con BCrypt hash, soporte para login local y Google (`google_sub`, `foto_url`). Columna `actualizacion TIMESTAMPTZ` se actualiza en cada UPDATE. Email indexado como `LOWER(email)` para unicidad case-insensitive.
- `SESIONES_USUARIOS` — sesiones activas con refresh token hasheado, IP y user-agent
- `VERIFICACION_EMAIL` — tokens de verificación (TTL configurable, default 24h)
- `RESET_PASSWORD` — tokens de reset (TTL configurable, default 1h)
- `INTENTOS_LOGIN` — registro de intentos fallidos para account lockout temporal. Indexado por `LOWER(email)` con UPSERT atómico.
- `AUDITORIA` — log de eventos de seguridad (LOGIN, RESET_CONTRASENA, CAMBIO_CONTRASENA, LOGOUT_ALL, REVOCACION_SESION) con IP, user-agent y timestamp.

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
- `Google.Apis.Auth` (v1.68.0) — validación de ID Tokens de Google para OAuth login
- `OpenTelemetry.Extensions.Hosting` + `OpenTelemetry.Instrumentation.AspNetCore` + `OpenTelemetry.Exporter.Prometheus.AspNetCore` — métricas expuestas en `/metrics`
- `Microsoft.Extensions.Caching.StackExchangeRedis` — cache de sesiones (con fallback a MemoryCache)

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
- `AuthWebAppFactory.cs` — levanta la app real con PostgreSQL en Docker (Testcontainers). Reemplaza `IEmailService` con `FakeEmailService` e `IHibpService` con `FakeHibpService`
- `FakeEmailService.cs` — fake de email con `ConcurrentBag` para capturar emails enviados en tests
- `FakeHibpService.cs` — fake de HIBP que siempre retorna `false` (no comprometida) para evitar llamadas HTTP reales
- `AuthIntegrationCollection.cs` — define la colección `"AuthIntegration"` con `ICollectionFixture<AuthWebAppFactory>`; garantiza que solo se crea UNA instancia de la factory compartida entre todas las clases de tests
- `AuthIntegrationTests.cs` — 33 tests de extremo a extremo sobre todos los endpoints
- `HealthCheckTests.cs` — 3 tests sobre `GET /health` (PostgreSQL disponible, Redis no configurado, PostgreSQL caído)
- `LockoutConcurrencyTests.cs` — 3 tests de concurrencia sobre el UPSERT de `INTENTOS_LOGIN`

Los integration tests requieren Docker corriendo. Usan `[Collection("AuthIntegration")]` (colección compartida) e `IAsyncLifetime` para setup/teardown asíncrono.

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
| POST | `/auth/google` | Público | 10 req/min por IP |
| POST | `/auth/resend-verification` | Público | 3 req/min por IP |
| GET | `/auth/me` | JWT requerido | — |
| POST | `/auth/change-password` | JWT requerido | — |
| GET | `/auth/confirm-change-password/{token}` | Público | — |
| POST | `/auth/logout` | JWT requerido | — |
| POST | `/auth/logout-all` | JWT requerido | — |
| GET | `/auth/sessions` | JWT requerido | — |
| POST | `/auth/sessions/revoke/{idSesion}` | JWT requerido | — |

El rate limiting es por IP (`RateLimitPartition.GetFixedWindowLimiter`), no global.

## Seguridad implementada

- **Refresh token rotation**: cada refresh invalida la sesión anterior y crea una nueva
- **Refresh token reuse detection**: si se usa un token ya invalidado, se revocan TODAS las sesiones del usuario (protección contra token robado)
- **Account lockout**: tras N intentos fallidos de login (configurable), la cuenta se bloquea por M minutos. UPSERT atómico en `INTENTOS_LOGIN` con índice en `LOWER(email)`.
- **Logout forzado al cambiar contraseña**: `ChangePasswordAsync` invalida TODAS las sesiones
- **Logout forzado al resetear contraseña**: `ResetPasswordAsync` también invalida todas las sesiones activas
- **Notificaciones de seguridad**: emails fire-and-forget al detectar nuevo login o cambio de contraseña
- **Prevención de enumeración de usuarios**: todos los caminos de fallo de login (usuario no existe, cuenta Google-only sin password, password incorrecto) retornan exactamente el mismo mensaje. `ForgotPasswordAsync` y `ResendVerificationAsync` también retornan siempre la misma respuesta
- **Tokens hasheados**: refresh tokens y tokens de email/reset se almacenan como SHA-256, nunca en texto plano
- **Docker no-root**: el contenedor corre con usuario `appuser` sin privilegios
- **CORS configurable**: en Development acepta cualquier origen; en producción lee `Cors:AllowedOrigins`
- **Google OAuth**: valida ID Tokens via `GoogleJsonWebSignature.ValidateAsync()` — nunca confía en datos del cliente
- **Security headers**: `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy` en todas las respuestas
- **Correlation ID**: header `X-Correlation-ID` propagado en todas las respuestas para trazabilidad
- **Email case-insensitive**: emails normalizados a lowercase antes de guardar/buscar; unicidad garantizada con índice `LOWER(email)`
- **Audit log**: eventos de seguridad registrados en tabla `AUDITORIA` de forma fire-and-forget (no bloquea el flujo principal si falla)
- **HaveIBeenPwned**: contraseñas verificadas contra filtraciones conocidas en `register` y `change-password` usando k-anonymity (SHA-1 prefix, nunca se envía la contraseña completa). Fail open: si HIBP no responde, no bloquea la operación.

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
  "Sesiones": {
    "MaxActivasPorUsuario": 4
  },
  "Lockout": {
    "MaxIntentos": 5,
    "MinutosBloqueo": 15
  },
  "Google": {
    "ClientId": "<client_id de Google Cloud Console>"
  },
  "App": {
    "BaseUrl": "https://tu-app.fly.dev"
  },
  "Email": {
    "Provider": "Resend",
    "ResendApiKey": "re_...",
    "FromAddress": "noreply@tudominio.com",
    "FromName": "AuthService",
    "Smtp": {
      "Host": "localhost",
      "Port": 1025,
      "EnableSsl": false,
      "User": "",
      "Password": ""
    }
  },
  "Redis": {
    "ConnectionString": ""
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

`Email:Provider` acepta `"Resend"` (producción) o `"Smtp"` (desarrollo local con MailHog/Mailtrap).

## CI/CD

**GitHub Actions** — dos workflows en `.github/workflows/`:

- `ci.yml`: se ejecuta en push/PR a `main`. Pasos: restore → build Release → test.
- `deploy.yml`: se ejecuta solo cuando `ci.yml` completa con éxito. Despliega a Fly.io con `flyctl deploy --remote-only`.

**Secreto requerido en GitHub**: `FLY_API_TOKEN` (generar con `fly tokens create deploy`).

El proyecto está desplegado en **Fly.io** (`fly.toml`, región `gru`, puerto 8080).

## Notas de contexto

- En entorno Development, `RegisterAsync` y `ForgotPasswordAsync` retornan la URL del token directamente en la respuesta (`verificar_url_dev`, `reset_url_dev`) para facilitar el testing sin email real.
- Swagger UI tiene un bug conocido con el botón Authorize en algunos entornos Windows — el header `Authorization` no se envía. Usar **Bruno** o **Postman** para probar endpoints protegidos.
- El endpoint `POST /auth/google` requiere un ID Token válido generado por el frontend usando Google Sign-In. Para obtener uno durante desarrollo, usar el Google OAuth Playground. El `Google:ClientId` debe configurarse en `appsettings.json`.
- `CleanupExpiredTokensService` corre cada hora como `IHostedService`. Usa `IServiceScopeFactory` para crear un scope temporal por ejecución (patrón obligatorio cuando un Singleton necesita servicios Scoped).
- Las notificaciones de login y cambio de contraseña son fire-and-forget (`_ = task.ContinueWith(...)`). Si fallan, solo se loguea un warning — no afectan el flujo principal.
- `ValidarSesionMiddleware` cachea sesiones activas en Redis (TTL 5 min). Si Redis no está configurado (`Redis:ConnectionString` vacío), cae de vuelta a `DistributedMemoryCache` automáticamente. Las operaciones que invalidan sesiones (logout, revoke, change-password) limpian el cache antes de tocar la BD.
- Las métricas de negocio se exponen en `GET /metrics` (formato Prometheus). Contadores: `auth_registrations_total`, `auth_logins_total`, `auth_token_refreshes_total`, todos con tag `result` para segmentar por resultado. En producción, `/metrics` está protegido.
- `PasswordPolicy` usa lista explícita de caracteres especiales en lugar de regex `[\W_]` — más predecible y documentable en la UI.
- Health checks en `GET /health`: `PostgresHealthCheck` verifica la conexión con una query `SELECT 1`; `RedisHealthCheck` verifica con `PING`. Retorna JSON con estado de cada dependency.
- Las llamadas a `AuditoriaRepository.RegistrarAsync` son fire-and-forget en `AutenticacionService`. Si fallan, se loguea un warning (`LogWarning`) — no afectan el flujo principal.
- El startup valida configuración crítica al arranque (Jwt:Key, ConnectionStrings:PostgresDb, etc.) y lanza `InvalidOperationException` si falta alguna. Fail-fast intencional.
- `HibpService` usa `HttpClient` tipado con timeout de 3 segundos y `User-Agent: AuthService/1.0`. Si la API no responde, `EsPasswordCompromisedAsync` retorna `false` (fail open) y loguea un `LogWarning`. En tests se usa `FakeHibpService` para evitar llamadas HTTP reales.
- `ResendVerificationAsync` siempre retorna el mismo mensaje independientemente de si el email existe o ya está verificado, evitando enumeración de usuarios. En Development incluye `verificar_url_dev` en la respuesta.
- `GET /auth/me` retorna `id`, `email`, `nombre`, `foto_url`, `email_verificado`, `proveedor_login` y `creacion`. No incluye `password_hash` ni `google_sub`.
- `POST /auth/reset-password` requiere los campos `token`, `newPassword` y `newPasswordConfirmacion`. El servidor valida que ambas contraseñas coincidan antes de procesar el reset.
- `GoogleLoginAsync` captura cualquier excepción de `GoogleJsonWebSignature.ValidateAsync` (no solo `InvalidJwtException`) y retorna 400 — tokens completamente malformados lanzaban `JsonException`/`FormatException` que no era `InvalidJwtException`.
- `POST /auth/change-password` inicia un flujo de confirmación por email: genera un token (TTL 30 min), lo persiste en `RESET_PASSWORD` con `tipo = 'change_confirm'` y guarda el BCrypt hash pre-computado de la nueva contraseña en `nuevo_password_hash`. `GET /auth/confirm-change-password/{token}` aplica el cambio y revoca todas las sesiones. El token de confirmación NO usa cache distribuida — es robusto frente a reinicios y despliegues multi-instancia.
