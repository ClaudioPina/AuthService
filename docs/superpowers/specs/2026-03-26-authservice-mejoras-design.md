# Diseño de Mejoras — AuthService
**Fecha:** 2026-03-26
**Autor:** Claudio Piña
**Estado:** Aprobado — pendiente de implementación

---

## Contexto

AuthService es un microservicio de autenticación construido en .NET 8 Minimal APIs + PostgreSQL. Fue creado con asistencia de IA y tiene una base arquitectónica sólida (JWT híbrido, sesiones en BD, BCrypt, patrón Repository), pero requiere un conjunto de mejoras para ser considerado production-ready y desplegable de forma profesional en Fly.io.

Este documento describe el alcance completo de las mejoras acordadas bajo el **Enfoque B: Refactor profesional + deploy**.

---

## Objetivos

1. Reestructurar el código para que sea mantenible y testeable.
2. Completar las funcionalidades que están incompletas (email real).
3. Limpiar el esquema de base de datos de residuos del sistema anterior.
4. Agregar las capas de seguridad y observabilidad que faltan para producción.
5. Agregar tests unitarios e integración completos.
6. Configurar CI/CD para deploy automático a Fly.io.
7. Documentar todo con comentarios exhaustivos (el desarrollador está aprendiendo .NET).

---

## Sección 1 — Estructura del código

### Problema actual
`Program.cs` tiene 667 líneas mezclando configuración de DI, middleware, y los 11 handlers de endpoints. Es imposible de testear unitariamente y difícil de mantener.

### Estructura propuesta

```
AuthService.Api/
├── Configuration/
│   └── SwaggerConfig.cs          # Configuración de Swagger extraída de Program.cs
├── Endpoints/
│   └── AuthEndpoints.cs          # Definición de rutas con MapGroup, sin lógica de negocio
├── Services/
│   ├── AuthService.cs            # Toda la lógica de negocio de autenticación
│   └── EmailService.cs           # Envío de emails via Resend SDK
├── Repositories/                 # Sin cambios estructurales
│   ├── UsuariosRepository.cs
│   ├── SesionesUsuariosRepository.cs
│   ├── VerificacionEmailRepository.cs
│   └── ResetPasswordRepository.cs
├── Models/                       # Sin cambios
├── DTOs/Auth/                    # Sin cambios
├── Middlewares/
│   └── ValidarSesionMiddleware.cs  # Sin cambios
├── Utils/
│   ├── JwtGenerator.cs
│   ├── PasswordHasher.cs
│   ├── PasswordPolicy.cs
│   └── TokenGenerator.cs
├── Data/
│   └── AppDbContext.cs           # Sin cambios
└── Program.cs                    # Solo: builder, DI, middleware pipeline, MapGroup (~80 líneas)
```

### Flujo de llamadas resultante

```
HTTP Request
    └── Program.cs (pipeline de middleware)
        └── AuthEndpoints.cs (recibe request, llama al service)
            └── AuthService.cs (lógica de negocio)
                ├── Repositories (acceso a datos PostgreSQL)
                │   └── AppDbContext (conexión con Polly retry)
                └── EmailService (envía emails via Resend)
                    └── Resend API
```

### Principio de responsabilidad única por capa

| Capa | Responsabilidad | Lo que NO debe hacer |
|------|----------------|----------------------|
| `AuthEndpoints.cs` | Mapear rutas, leer el request, llamar al service, retornar el resultado | Lógica de negocio, acceso a BD |
| `AuthService.cs` | Orquestar la lógica (validaciones, reglas, coordinación) | Queries SQL directas |
| `Repositories` | Queries SQL, mapeo de resultados | Reglas de negocio |
| `AppDbContext` | Gestión de conexiones con retry | Nada más |

### Program.cs resultante (~80 líneas)
Solo contendrá:
- Configuración de JWT
- Configuración de Swagger (delegada a `SwaggerConfig.cs`)
- Registro de DI (repositorios, services)
- Pipeline de middleware (auth, cors, rate limiting, health checks, serilog)
- `app.MapAuthEndpoints()` — una sola línea que registra todas las rutas

---

## Sección 2 — Base de datos

### Problemas a corregir

**a) Columnas residuales de multi-tenancy:**
Las 4 tablas tienen `propietario` (siempre hardcodeada como `1`) y `usuario` (nunca usada). Son remanentes de un sistema anterior acoplado a un dominio de negocio específico. En un microservicio de auth independiente no tienen sentido.

**b) INSERTs comentados de tablas ajenas:**
El script contiene INSERTs comentados para `TIPO_TRANSACCIONES`, `ORGANIZACION`, `CATEGORIAS`, `TRANSACCIONES`, etc. No tienen sus `CREATE TABLE` correspondientes — el script fallaría en una BD limpia. Son datos de otro sistema.

**c) Datos personales en comentarios:**
Email y nombre del autor en el INSERT de usuario admin comentado.

### Esquema limpio resultante

```sql
-- USUARIOS
CREATE TABLE USUARIOS (
    id_usuario       SERIAL        PRIMARY KEY,
    email            VARCHAR(150)  NOT NULL UNIQUE,
    nombre           VARCHAR(100),
    foto_url         VARCHAR(300),
    password_hash    VARCHAR(200),
    proveedor_login  VARCHAR(30)   CHECK (proveedor_login IN ('LOCAL', 'GOOGLE', 'MIXTO')),
    google_sub       VARCHAR(60)   UNIQUE,
    email_verificado SMALLINT      DEFAULT 0 NOT NULL CHECK (email_verificado IN (0, 1)),
    creacion         TIMESTAMP     DEFAULT CURRENT_TIMESTAMP NOT NULL,
    estado           SMALLINT      DEFAULT 1 NOT NULL CHECK (estado IN (0, 1))
);

-- SESIONES_USUARIOS
CREATE TABLE SESIONES_USUARIOS (
    id_sesion     SERIAL       PRIMARY KEY,
    id_usuario    INTEGER      NOT NULL REFERENCES USUARIOS (id_usuario),
    token_refresh VARCHAR(300) NOT NULL,
    expira_en     TIMESTAMP    NOT NULL,
    user_agent    VARCHAR(300),
    ip_origen     VARCHAR(45),  -- IPv6 puede tener hasta 45 caracteres
    creacion      TIMESTAMP    DEFAULT CURRENT_TIMESTAMP NOT NULL,
    estado        SMALLINT     DEFAULT 1 NOT NULL CHECK (estado IN (0, 1))
);

-- VERIFICACION_EMAIL
CREATE TABLE VERIFICACION_EMAIL (
    id_verificacion SERIAL       PRIMARY KEY,
    id_usuario      INTEGER      NOT NULL REFERENCES USUARIOS (id_usuario),
    token           VARCHAR(200) NOT NULL,
    expira_en       TIMESTAMP    NOT NULL,
    creacion        TIMESTAMP    DEFAULT CURRENT_TIMESTAMP NOT NULL,
    estado          SMALLINT     DEFAULT 1 NOT NULL CHECK (estado IN (0, 1))
);

-- RESET_PASSWORD
CREATE TABLE RESET_PASSWORD (
    id_reset   SERIAL       PRIMARY KEY,
    id_usuario INTEGER      NOT NULL REFERENCES USUARIOS (id_usuario),
    token      VARCHAR(200) NOT NULL,
    expira_en  TIMESTAMP    NOT NULL,
    creacion   TIMESTAMP    DEFAULT CURRENT_TIMESTAMP NOT NULL,
    estado     SMALLINT     DEFAULT 1 NOT NULL CHECK (estado IN (0, 1))
);
```

**Índices mantenidos:**
- `IDX_USUARIOS_PROVEEDOR_LOGIN`
- `IDX_SESIONES_USUARIOS_ID_USUARIO`
- `IDX_VERIFEMAIL_ID_USUARIO`, `IDX_VERIFEMAIL_TOKEN`
- `IDX_RESETPASS_ID_USUARIO`, `IDX_RESETPASS_TOKEN`

### Impacto en repositorios
Los 4 repositorios deben actualizar sus queries `INSERT` para eliminar los parámetros `propietario` y `usuario`. Es un cambio puntual en cada archivo.

---

## Sección 3 — Funcionalidades y correcciones

### 3.1 — Email real con Resend

**NuGet:** `Resend` (SDK oficial de Resend para .NET)

**Comportamiento por ambiente:**

| Ambiente | Comportamiento |
|----------|---------------|
| `Development` | El link de verificación/reset se devuelve en la respuesta de la API (útil para testing sin email) |
| `Production` | El link se envía por correo. No aparece en la respuesta de la API |

**Configuración en `appsettings.json`:**
```json
"Email": {
  "ResendApiKey": "re_xxxxxxxxxxxxx",
  "FromAddress": "onboarding@resend.dev",
  "FromName": "AuthService"
}
```

> **Nota:** Mientras no tengas dominio propio configurado en Resend, usar `onboarding@resend.dev` como remitente. Esto funciona en el free tier de Resend. Cuando configures tu dominio, solo cambia `FromAddress` en la configuración.

**`EmailService.cs` tendrá dos métodos:**
- `SendVerificationEmailAsync(string toEmail, string verificationLink)`
- `SendPasswordResetEmailAsync(string toEmail, string resetLink)`

### 3.2 — Rate Limiting

**Implementación:** Rate limiter nativo de .NET 8 (`Microsoft.AspNetCore.RateLimiting`, incluido en el framework, sin NuGet extra).

**Políticas:**

| Endpoint | Límite | Ventana |
|----------|--------|---------|
| `POST /auth/login` | 10 requests | 1 minuto por IP |
| `POST /auth/register` | 5 requests | 1 minuto por IP |
| `POST /auth/forgot-password` | 3 requests | 1 minuto por IP |

- Retorna `429 Too Many Requests` con header `Retry-After` al exceder el límite.
- Los demás endpoints no tienen rate limiting (no son vectores de ataque por fuerza bruta).

### 3.3 — CORS

Configuración de orígenes permitidos en `appsettings.json`:

```json
"Cors": {
  "AllowedOrigins": ["http://localhost:5173", "https://miapp.com"]
}
```

- En `Development`: permite cualquier `localhost` automáticamente.
- En `Production`: solo los dominios listados explícitamente.

### 3.4 — Health Check

```http
GET /health
```

Respuesta:
```json
{ "status": "healthy" }
```

- Sin autenticación.
- Fly.io usa este endpoint para determinar si la máquina está lista para recibir tráfico.
- No expone información sensible del sistema.

### 3.5 — Logging con Serilog

**NuGet:** `Serilog.AspNetCore` + `Serilog.Sinks.Console`

- Reemplaza el logger default de .NET.
- Logs estructurados en formato JSON a consola → Fly.io los captura y permite búsqueda.
- Niveles: `Information` en producción, `Debug` en desarrollo.

**Eventos que se registran:**
- Inicio de la aplicación con versión y ambiente.
- Cada request HTTP (método, ruta, status code, duración).
- Login fallido (sin revelar si el email existe — solo "intento fallido desde IP X").
- Sesión creada / invalidada / revocada.
- Errores no manejados con stack trace completo.

### 3.6 — Configuración limpia

**Valores que se mueven a `appsettings.json`:**

```json
{
  "Jwt": {
    "Key": "...",
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
    "BaseUrl": "https://authservice.fly.dev"
  },
  "Email": {
    "ResendApiKey": "...",
    "FromAddress": "onboarding@resend.dev",
    "FromName": "AuthService"
  },
  "Cors": {
    "AllowedOrigins": ["http://localhost:5173"]
  }
}
```

**`appsettings.example.json`** (sin secretos, se sube al repositorio):
Misma estructura pero con valores de ejemplo y comentarios explicativos. Sirve como guía de onboarding.

**Swagger:** Solo disponible en ambiente `Development`. En producción queda deshabilitado.

### 3.7 — Dockerfile hardening

Agregar usuario no-root en la imagen de producción. Buena práctica de seguridad: si hay una vulnerabilidad en la app, el proceso no corre como root dentro del contenedor.

```dockerfile
# Etapa runtime: crear usuario sin privilegios
RUN adduser --disabled-password --gecos "" appuser
USER appuser
```

---

## Sección 4 — Testing

### Proyecto: `AuthService.Tests`

Agregado a la solución `AuthService.sln` como proyecto `xUnit`.

**NuGet del proyecto de tests:**
```xml
<PackageReference Include="xunit" />
<PackageReference Include="xunit.runner.visualstudio" />
<PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" />
<PackageReference Include="Testcontainers.PostgreSql" />
<PackageReference Include="FluentAssertions" />
```

> `Testcontainers.PostgreSql` levanta una instancia real de PostgreSQL en Docker durante los tests de integración. No requiere tener PostgreSQL instalado localmente — solo Docker.

### 4.1 — Tests unitarios

Sin base de datos ni red. Se ejecutan en milisegundos.

| Clase | Casos de prueba |
|-------|----------------|
| `PasswordPolicyTests` | Contraseña válida pasa, sin mayúscula falla, sin símbolo falla, menos de 8 caracteres falla, sin número falla |
| `PasswordHasherTests` | El hash es diferente al texto plano, verificación correcta retorna true, verificación incorrecta retorna false |
| `TokenGeneratorTests` | Token tiene longitud esperada, dos tokens generados son distintos, hash del mismo token es siempre igual |
| `JwtGeneratorTests` | JWT contiene claim `id`, contiene claim `email`, contiene claim `id_sesion`, expiración es ~15 minutos desde ahora |

### 4.2 — Tests de integración

Usan `WebApplicationFactory<Program>` para levantar el servidor completo en memoria con PostgreSQL real via Testcontainers.

| Test | Endpoint | Escenario | Resultado esperado |
|------|----------|-----------|-------------------|
| `Register_WithValidData_Returns201` | `POST /auth/register` | Datos válidos | 201 Created |
| `Register_WithDuplicateEmail_Returns409` | `POST /auth/register` | Email ya registrado | 409 Conflict |
| `Register_WithWeakPassword_Returns400` | `POST /auth/register` | Contraseña sin símbolo | 400 Bad Request |
| `Login_WithValidCredentials_ReturnsTokens` | `POST /auth/login` | Usuario verificado, credenciales correctas | 200 con accessToken y refreshToken |
| `Login_WithWrongPassword_Returns400` | `POST /auth/login` | Contraseña incorrecta | 400 con mensaje genérico |
| `Login_WithUnverifiedEmail_Returns400` | `POST /auth/login` | Email sin verificar | 400 con mensaje específico |
| `RefreshToken_WithValidToken_ReturnsNewTokens` | `POST /auth/refresh-token` | Token válido | 200 con nuevos tokens |
| `RefreshToken_WithExpiredToken_Returns400` | `POST /auth/refresh-token` | Token expirado | 400 Bad Request |
| `Logout_WithValidJwt_InvalidatesSession` | `POST /auth/logout` | JWT válido | 200, sesión inactiva en BD |
| `LogoutAll_WithValidJwt_InvalidatesAllSessions` | `POST /auth/logout-all` | JWT válido | 200, todas las sesiones inactivas |
| `ChangePassword_WithCorrectCurrentPassword_Succeeds` | `POST /auth/change-password` | Contraseña actual correcta | 200, sesiones invalidadas |
| `Sessions_WithoutJwt_Returns401` | `GET /auth/sessions` | Sin Authorization header | 401 Unauthorized |
| `Health_Returns200` | `GET /health` | Sin autenticación | 200 `{"status":"healthy"}` |

### Comando para ejecutar tests

```bash
# Todos los tests
dotnet test

# Solo unitarios
dotnet test --filter "Category=Unit"

# Solo integración
dotnet test --filter "Category=Integration"

# Con output detallado
dotnet test --logger "console;verbosity=detailed"
```

> **Prerequisito para tests de integración:** Docker debe estar corriendo localmente. Testcontainers lo usa para levantar PostgreSQL automáticamente.

---

## Sección 5 — CI/CD y Deployment

### 5.1 — GitHub Actions

#### `.github/workflows/ci.yml`
Se ejecuta en cada push y pull request a `main`.

```
Pasos:
1. Checkout del código
2. Setup .NET 8
3. dotnet restore
4. dotnet build --no-restore
5. dotnet test --no-build (incluye Testcontainers — requiere Docker en el runner)
```

El runner de GitHub Actions tiene Docker disponible por defecto, así que Testcontainers funciona sin configuración extra.

#### `.github/workflows/deploy.yml`
Se ejecuta solo cuando `ci.yml` pasa exitosamente en `main`.

```
Pasos:
1. Checkout del código
2. Setup flyctl (CLI de Fly.io)
3. fly deploy --remote-only
   (--remote-only: el build Docker ocurre en los servidores de Fly.io, no en GitHub)
```

**Secret requerido en GitHub:**
- `FLY_API_TOKEN` → generado con `fly tokens create deploy`

### 5.2 — Setup de Fly.io (pasos únicos antes del primer deploy)

```bash
# 1. Autenticarse en Fly.io
fly auth login

# 2. Crear la base de datos PostgreSQL (solo una vez)
fly postgres create --name authservice-db --region gru

# 3. Conectar la BD a la aplicación (inyecta DATABASE_URL automáticamente)
fly postgres attach authservice-db --app authservice

# 4. Configurar los secrets de la aplicación
fly secrets set \
  Jwt__Key="tu_clave_de_256_bits_aqui" \
  Jwt__Issuer="AuthService" \
  Jwt__Audience="AuthServiceClients" \
  Email__ResendApiKey="re_xxxxxxxxxxxxxx" \
  App__BaseUrl="https://authservice.fly.dev"

# 5. Primer deploy manual
fly deploy
```

> **Nota sobre la connection string:** Al hacer `fly postgres attach`, Fly.io inyecta automáticamente la variable de entorno `DATABASE_URL`. El código debe leer esa variable si existe, o caer en `ConnectionStrings:PostgresDb` del appsettings como fallback.

### 5.3 — Generación del FLY_API_TOKEN para GitHub Actions

```bash
# Generar token de deploy (solo permisos de deploy, no de administración)
fly tokens create deploy

# Copiar el token generado y guardarlo en:
# GitHub → Settings → Secrets and variables → Actions → New repository secret
# Nombre: FLY_API_TOKEN
# Valor: (el token copiado)
```

### 5.4 — Archivos nuevos en el repositorio

```
.github/
└── workflows/
    ├── ci.yml           # Build + tests en cada push/PR
    └── deploy.yml       # Deploy a Fly.io en merge a main
appsettings.example.json # Estructura de configuración sin secretos
```

---

## Resumen de cambios por archivo

### Archivos modificados
| Archivo | Cambio |
|---------|--------|
| `Program.cs` | Reducir a ~80 líneas: solo DI, middleware, mapeo de grupos |
| `script_DB.sql` | Eliminar columnas residuales, eliminar INSERTs ajenos |
| `Dockerfile` | Agregar usuario no-root |
| `fly.toml` | Sin cambios (ya está correcto) |
| `Repositories/*.cs` | Actualizar INSERTs para eliminar `propietario` y `usuario` |

### Archivos nuevos
| Archivo | Propósito |
|---------|-----------|
| `Configuration/SwaggerConfig.cs` | Configuración de Swagger extraída |
| `Endpoints/AuthEndpoints.cs` | Definición de todas las rutas |
| `Services/AuthService.cs` | Lógica de negocio de autenticación |
| `Services/EmailService.cs` | Envío de emails via Resend |
| `appsettings.example.json` | Template de configuración sin secretos |
| `.github/workflows/ci.yml` | Pipeline de CI |
| `.github/workflows/deploy.yml` | Pipeline de CD |
| `AuthService.Tests/` | Proyecto de tests (unitarios + integración) |

---

## Dependencias NuGet nuevas

| Paquete | Proyecto | Propósito |
|---------|----------|-----------|
| `Resend` | `AuthService.Api` | SDK oficial de Resend para envío de emails |
| `Serilog.AspNetCore` | `AuthService.Api` | Logging estructurado |
| `Serilog.Sinks.Console` | `AuthService.Api` | Output de logs a consola (capturado por Fly.io) |
| `xunit` | `AuthService.Tests` | Framework de tests |
| `xunit.runner.visualstudio` | `AuthService.Tests` | Integración con Visual Studio / VS Code |
| `Microsoft.AspNetCore.Mvc.Testing` | `AuthService.Tests` | WebApplicationFactory para integration tests |
| `Testcontainers.PostgreSql` | `AuthService.Tests` | PostgreSQL real en Docker para tests |
| `FluentAssertions` | `AuthService.Tests` | Asserts más legibles en tests |

> `Microsoft.AspNetCore.RateLimiting` ya viene incluido en .NET 8, no requiere NuGet extra.

---

## Consideraciones de aprendizaje

Dado que el desarrollador está aprendiendo .NET, todo el código generado incluirá:

- **Comentarios en cada método** explicando qué hace y por qué.
- **Comentarios en bloques de lógica no obvia** (ej: por qué se hashea el refresh token antes de guardarlo).
- **Comentarios en decisiones de seguridad** (ej: por qué el mensaje de login es genérico).
- **Comentarios en la configuración** de Serilog, rate limiting, CORS, y JWT.
- **Notas en los tests** explicando qué patrón se está usando (Arrange/Act/Assert).

---

## Stack final

| Capa | Tecnología |
|------|-----------|
| Runtime | .NET 8, C# 12 |
| API | Minimal APIs |
| Base de datos | PostgreSQL (Fly.io Postgres) |
| Driver BD | Npgsql + Polly retry |
| Auth | JWT + BCrypt + Refresh Tokens |
| Email | Resend SDK |
| Logging | Serilog |
| Rate Limiting | Microsoft.AspNetCore.RateLimiting (built-in) |
| Tests | xUnit + Testcontainers + FluentAssertions |
| Contenedor | Docker (imagen ASP.NET 8, usuario no-root) |
| Deploy | Fly.io (región gru, HTTPS forzado) |
| CI/CD | GitHub Actions |
