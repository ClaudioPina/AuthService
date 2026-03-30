# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Comandos principales

```bash
# Build
dotnet build

# Ejecutar en desarrollo (HTTP: localhost:5091, HTTPS: localhost:7075)
dotnet run --project AuthService.Api

# Build Docker
docker build -t authservice .
docker run -p 8080:8080 authservice
```

No hay proyecto de tests configurado aún. El testing manual se hace via Swagger (`/swagger`) o el archivo `AuthService.Api.http`.

## Arquitectura

**AuthService** es un microservicio de autenticación independiente en .NET 8 Minimal APIs. No usa Controllers: todos los endpoints están definidos directamente en `Program.cs`.

### Modelo de autenticación híbrido

- **Stateless**: JWT Access Token (15 min) enviado como Bearer header. Claims: `id`, `email`, `id_sesion`.
- **Stateful**: Refresh Token hasheado + sesión en base de datos. Permite revocación.
- **Session validation**: `ValidarSesionMiddleware` intercepta todas las rutas protegidas y verifica el `id_sesion` del JWT contra la tabla `SESIONES_USUARIOS` en PostgreSQL.

### Capas

```
Program.cs (routes + DI)
    └── Repositories (acceso a datos via Npgsql raw SQL)
         └── AppDbContext (Npgsql + Polly retry, 3 intentos, 300ms)
```

No hay capa de Services separada — la lógica de negocio vive directamente en los handlers de `Program.cs`. Si la complejidad crece, extraer a una capa `Services/`.

### Carpetas clave

| Carpeta | Propósito |
|---------|-----------|
| `Data/` | `AppDbContext` — manejo de conexiones con retry via Polly |
| `Models/` | Entidades: `Usuario`, `SesionUsuario`, `RefreshToken`, `ResetPasswordToken` |
| `DTOs/Auth/` | DTOs de request para cada endpoint |
| `Repositories/` | Acceso a datos (queries SQL directas con Npgsql) |
| `Middlewares/` | `ValidarSesionMiddleware` — valida sesión activa en DB |
| `Utils/` | `JwtGenerator`, `PasswordHasher` (BCrypt), `PasswordPolicy`, `TokenGenerator` |

## Base de datos

**PostgreSQL** — sin Entity Framework Migrations. El esquema se gestiona con `script_DB.sql`.

Tablas principales:
- `USUARIOS` — cuentas de usuario con BCrypt hash
- `SESIONES_USUARIOS` — sesiones activas con refresh token hasheado, IP y user-agent
- `VERIFICACION_EMAIL` — tokens de verificación (24h TTL)
- `RESET_PASSWORD` — tokens de reset (1h TTL)

**Al modificar el esquema**: actualizar `script_DB.sql` y los repositories correspondientes. No hay migraciones automáticas.

Convención SQL del proyecto: parámetros con `@param` (Npgsql), nombres de tablas y columnas en `UPPER_SNAKE_CASE`.

## Dependencias NuGet relevantes

- `BCrypt.Net-Next` — hashing de passwords
- `Npgsql` — driver PostgreSQL (sin EF Core)
- `System.IdentityModel.Tokens.Jwt` + `Microsoft.AspNetCore.Authentication.JwtBearer` — JWT
- `Polly` — retry policy para conexiones DB
- `Swashbuckle.AspNetCore` — Swagger/OpenAPI

## Endpoints

| Método | Ruta | Autenticación |
|--------|------|---------------|
| POST | `/auth/register` | Público |
| POST | `/auth/login` | Público |
| GET | `/auth/verify-email/{token}` | Público |
| POST | `/auth/forgot-password` | Público |
| POST | `/auth/reset-password` | Público |
| POST | `/auth/refresh-token` | Público |
| POST | `/auth/change-password` | JWT requerido |
| POST | `/auth/logout` | JWT requerido |
| POST | `/auth/logout-all` | JWT requerido |
| GET | `/auth/sessions` | JWT requerido |
| POST | `/auth/sessions/revoke/{idSesion}` | JWT requerido |

## Configuración esperada (appsettings.json)

El archivo no está en el repo (excluido por seguridad). Estructura requerida:

```json
{
  "Jwt": {
    "Key": "<256-bit secret>",
    "Issuer": "AuthService",
    "Audience": "AuthServiceClients"
  },
  "ConnectionStrings": {
    "PostgresDb": "Host=...;Database=...;Username=...;Password=..."
  }
}
```

## Notas de contexto

- El proyecto migró de Oracle a PostgreSQL (documentado en `implementation_plan.md`).
- `script_DB.sql` contiene tablas residuales de negocio (`TRANSACCIONES`, `ORGANIZACION`) que no pertenecen al microservicio de auth — ignorarlas.
- Hay columnas de multi-tenancy (`propietario`) que son remanentes de la arquitectura anterior.
- Los valores hardcodeados (URLs de verificación, TTL de tokens) están pendientes de mover a `appsettings.json`.
- El proyecto está desplegado en Fly.io (`fly.toml`, región `gru`, puerto 8080).
