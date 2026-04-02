# 🔐 AuthService – Microservicio de Autenticación

**.NET 8 + JWT + Refresh Tokens + PostgreSQL**

AuthService es un microservicio independiente en **.NET 8 Minimal APIs** responsable exclusivamente de autenticación, autorización y gestión de sesiones de usuarios.

Está diseñado para ser consumido por aplicaciones web (SPA), móviles, otros microservicios o APIs internas.

**No contiene lógica de negocio. Su única responsabilidad es identidad y seguridad.**

---

## Tabla de contenidos

- [Responsabilidades](#-responsabilidades)
- [Arquitectura](#-arquitectura)
- [Modelo de autenticación híbrido](#-modelo-de-autenticación-híbrido)
- [Seguridad](#-seguridad)
- [Tecnologías](#-tecnologías)
- [Estructura del proyecto](#-estructura-del-proyecto)
- [Tests](#-tests)
- [Configuración](#-configuración)
- [Cómo ejecutar](#-cómo-ejecutar)
- [Endpoints](#-endpoints)
- [CI/CD y despliegue](#-cicd-y-despliegue)
- [Autor](#-autor)

---

## 🎯 Responsabilidades

- Registro de usuarios y verificación de email
- Login con credenciales (email + password)
- Login con Google OAuth (ID Token)
- Emisión de Access Tokens (JWT, 15 min)
- Emisión y rotación de Refresh Tokens (7 días)
- Recuperación y reset de contraseña por email
- Cambio de contraseña con cierre forzado de todas las sesiones
- Manejo de sesiones múltiples (por dispositivo/cliente)
- Logout individual y logout global
- Revocación de sesiones específicas
- Validación de sesiones activas en cada request
- Bloqueo temporal de cuenta tras intentos fallidos de login
- Notificaciones por email en nuevos logins y cambios de contraseña
- Limpieza automática de tokens y sesiones expirados
- Cache de sesiones activas (Redis / MemoryCache) para reducir queries a BD
- Métricas de negocio expuestas en `/metrics` (formato Prometheus)
- Health checks de dependencias expuestos en `GET /health` (PostgreSQL + Redis)
- Audit log de eventos de seguridad en tabla `AUDITORIA`

**Lo que AuthService NO hace:** no maneja lógica de negocio, no almacena datos de dominio, no gestiona permisos específicos de la aplicación.

---

## 🧱 Arquitectura

```text
[ Cliente / Frontend ]
        |
        v
[ AuthEndpoints (rutas Minimal API) ]
        |
        v
[ AutenticacionService (lógica de negocio) ]
        |
        +---> [ IEmailService → EmailService (Resend) / SmtpEmailService (local) ]
        |
        v
[ Repositories (Npgsql raw SQL) ]
        |
        v
[ PostgreSQL ]

[ CleanupExpiredTokensService (BackgroundService) ] → Repositories (cada 1h)
```

El proyecto sigue una arquitectura en capas sin Entity Framework:

| Capa | Descripción |
|------|-------------|
| `Endpoints/` | Define las rutas y extrae claims del JWT |
| `Services/` | Contiene toda la lógica de negocio |
| `Repositories/` | Acceso a datos con queries SQL directas (Npgsql) |
| `Data/` | Manejo de conexiones con retry automático (Polly) |
| `Middlewares/` | Validación de sesión activa en cada request autenticado |
| `Utils/` | JWT, hashing de passwords, política de contraseñas, tokens |
| `Configuration/` | Configuración de Swagger con soporte JWT Bearer |

---

## 🔐 Modelo de autenticación híbrido

AuthService combina autenticación **stateless** y **stateful**:

| Token | Tipo | Duración | Almacenamiento |
|-------|------|----------|----------------|
| Access Token (JWT) | Stateless | 15 min | Solo en cliente |
| Refresh Token | Stateful | 7 días | Hash SHA-256 en BD |

### Flujo de Login

```
Cliente → POST /auth/login
  → Valida credenciales
  → Crea sesión en BD (con IP y user-agent)
  → Genera access_token (JWT con claims: id, email, id_sesion)
  → Genera refresh_token (plain) → almacena hash en BD
  → Retorna ambos tokens al cliente
```

### Flujo de Refresh Token

```
Cliente → POST /auth/refresh-token (con refresh_token)
  → Hashea el token recibido → busca en BD
  → Invalida la sesión anterior
  → Crea nueva sesión con nuevos tokens
  → Retorna nuevos access_token y refresh_token
```

### Validación de sesión (Middleware)

Cada request a rutas protegidas pasa por `ValidarSesionMiddleware`:
1. Extrae el claim `id_sesion` del JWT
2. Consulta `SESIONES_USUARIOS` en BD
3. Si la sesión no existe o está inactiva → responde `401`

Esto permite invalidar JWTs sin esperar a que expiren (logout, cambio de contraseña, revocación).

---

## 🔒 Seguridad

- **BCrypt** para hashing de contraseñas
- **Política de contraseñas**: mínimo 8 caracteres, al menos 1 mayúscula, 1 minúscula, 1 número, 1 símbolo
- **Tokens nunca en texto plano**: refresh tokens, tokens de verificación y reset se almacenan como SHA-256
- **Refresh token rotation**: cada uso del refresh invalida la sesión anterior
- **Refresh token reuse detection**: si se detecta un token ya usado, se revocan TODAS las sesiones activas del usuario
- **Account lockout**: bloqueo temporal configurable tras N intentos fallidos (default: 5 intentos, 15 min)
- **Logout forzado al cambiar contraseña**: invalida todas las sesiones activas
- **Notificaciones de seguridad**: email al detectar nuevo login desde IP nueva o cambio de contraseña
- **Prevención de enumeración de usuarios**: `forgot-password` siempre retorna la misma respuesta
- **Rate limiting por IP**: límites separados para register, login, google y forgot-password
- **Google OAuth**: validación de ID Tokens server-side vía `Google.Apis.Auth` — no se confía en datos del cliente
- **CORS configurable**: permisivo en Development, restringido en producción
- **Cache de sesiones**: Redis (TTL 5 min) reduce queries a BD en cada request autenticado; fallback automático a MemoryCache si Redis no está configurado
- **Security headers**: `X-Content-Type-Options`, `X-Frame-Options`, `X-XSS-Protection`, `Referrer-Policy` en todas las respuestas
- **Correlation ID**: header `X-Correlation-ID` propagado en cada respuesta para trazabilidad entre servicios
- **Email case-insensitive**: emails normalizados a lowercase; unicidad garantizada con índice parcial `LOWER(email)` en PostgreSQL
- **Logout forzado al resetear contraseña**: `ResetPasswordAsync` invalida todas las sesiones activas además de cambiar la contraseña
- **Audit log**: eventos de seguridad (login, cambio/reset de contraseña, logout-all, revocación de sesión) registrados en tabla `AUDITORIA` de forma asíncrona fire-and-forget
- **Docker non-root**: el contenedor corre con usuario `appuser` sin privilegios

---

## 🚀 Tecnologías

| Tecnología | Uso |
|------------|-----|
| .NET 8 Minimal APIs | Framework principal |
| PostgreSQL + Npgsql | Base de datos (sin EF Core) |
| JWT Bearer | Access tokens |
| BCrypt.Net-Next | Hashing de contraseñas |
| Polly | Retry automático en conexiones a BD |
| Resend SDK | Envío de emails transaccionales (producción) |
| System.Net.Mail | Envío de emails SMTP (desarrollo local) |
| Google.Apis.Auth | Validación de ID Tokens de Google OAuth |
| OpenTelemetry + Prometheus | Métricas de negocio en `/metrics` |
| StackExchange.Redis | Cache de sesiones (con fallback a MemoryCache) |
| Serilog | Logging estructurado |
| Swashbuckle | Swagger / OpenAPI |
| Fly.io | Hosting en producción |
| Docker | Containerización |
| GitHub Actions | CI/CD |
| xUnit + Testcontainers | Testing con PostgreSQL real |

---

## 🗂️ Estructura del proyecto

```
AuthService/
├── .github/
│   └── workflows/
│       ├── ci.yml              # Build + tests en cada push/PR
│       └── deploy.yml          # Deploy a Fly.io (solo si CI pasa)
│
├── AuthService.Api/
│   ├── Configuration/
│   │   └── SwaggerConfig.cs    # Swagger con JWT Bearer
│   ├── Data/
│   │   └── AppDbContext.cs     # Conexión con Polly retry
│   ├── DTOs/Auth/              # DTOs de request por endpoint
│   ├── Endpoints/
│   │   └── AuthEndpoints.cs    # Definición de las 12 rutas
│   ├── Middlewares/
│   │   └── ValidarSesionMiddleware.cs
│   ├── Models/                 # Entidades (Usuario, SesionUsuario, IntentoLogin, etc.)
│   ├── HealthChecks/
│   │   ├── PostgresHealthCheck.cs
│   │   └── RedisHealthCheck.cs
│   ├── Repositories/           # Acceso a datos (Npgsql raw SQL)
│   │   ├── UsuariosRepository.cs
│   │   ├── SesionesUsuariosRepository.cs
│   │   ├── VerificacionEmailRepository.cs
│   │   ├── ResetPasswordRepository.cs
│   │   ├── IntentosLoginRepository.cs
│   │   └── AuditoriaRepository.cs
│   ├── Services/
│   │   ├── IAutenticacionService.cs
│   │   ├── AutenticacionService.cs
│   │   ├── IEmailService.cs
│   │   ├── EmailService.cs                 # Resend SDK (producción)
│   │   ├── SmtpEmailService.cs             # SMTP (desarrollo local)
│   │   ├── CleanupExpiredTokensService.cs  # BackgroundService (limpieza cada 1h)
│   │   └── AuthMetrics.cs                  # Contadores OpenTelemetry (/metrics)
│   ├── Utils/
│   │   ├── JwtGenerator.cs
│   │   ├── PasswordHasher.cs
│   │   ├── PasswordPolicy.cs
│   │   └── TokenGenerator.cs
│   └── Program.cs              # DI, middleware pipeline, configuración
│
├── AuthService.Tests/
│   ├── Unit/                   # Tests unitarios (no requieren Docker)
│   │   ├── JwtGeneratorTests.cs
│   │   ├── PasswordHasherTests.cs
│   │   ├── PasswordPolicyTests.cs
│   │   └── TokenGeneratorTests.cs
│   └── Integration/            # Tests de integración (requieren Docker)
│       ├── AuthWebAppFactory.cs
│       ├── FakeEmailService.cs
│       └── AuthIntegrationTests.cs
│
├── appsettings.example.json    # Plantilla de configuración (sin secretos)
├── script_DB.sql               # Esquema de base de datos
├── Dockerfile
└── fly.toml
```

---

## 🧪 Tests

El proyecto tiene 41 tests en total:

| Tipo | Archivo | Tests |
|------|---------|-------|
| Unit | `PasswordPolicyTests.cs` | 7 |
| Unit | `PasswordHasherTests.cs` | 4 |
| Unit | `TokenGeneratorTests.cs` | 5 |
| Unit | `JwtGeneratorTests.cs` | 3 |
| Integration | `AuthIntegrationTests.cs` | 22 |

Los **integration tests** levantan la aplicación real con una instancia de PostgreSQL en Docker (via Testcontainers) y reemplazan el `IEmailService` con un fake en memoria para capturar los emails enviados.

```bash
# Solo unitarios (rápidos, sin Docker)
dotnet test --filter "FullyQualifiedName~Unit"

# Solo integración (requiere Docker)
dotnet test --filter "FullyQualifiedName~Integration"
```

---

## ⚙️ Configuración

> **El archivo `appsettings.json` no está en el repositorio.** Crea uno basándote en `appsettings.example.json`.

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

Para desarrollo local con SMTP, usar `"Email:Provider": "Smtp"` apuntando a MailHog o Mailtrap.

En producción (Fly.io), las variables sensibles se configuran como secrets:

```bash
fly secrets set "Jwt__Key=..." "Email__ResendApiKey=re_..." "App__BaseUrl=https://..."
```

---

## ▶️ Cómo ejecutar

```bash
# Desarrollo local
dotnet run --project AuthService.Api
```

Swagger disponible en `http://localhost:5091/swagger` (solo en entorno Development).

Para probar endpoints protegidos se recomienda usar **Bruno** o **Postman** — Swagger UI tiene un bug conocido en algunos entornos Windows donde el header `Authorization` no se envía correctamente.

1. Registrar usuario: `POST /auth/register`
2. Verificar email con el link del correo (o usar `verificar_url_dev` en la respuesta en Development)
3. Login: `POST /auth/login` → copiar `accessToken`
4. En Bruno/Postman: configurar Auth → Bearer Token → pegar `accessToken`

```bash
# Docker
docker build -t authservice .
docker run -p 8080:8080 \
  -e "Jwt__Key=..." \
  -e "ConnectionStrings__PostgresDb=..." \
  authservice
```

---

## 🔥 Endpoints

### Públicos

| Método | Ruta | Descripción | Rate Limit |
|--------|------|-------------|------------|
| POST | `/auth/register` | Registro con email + password | 5 req/min por IP |
| POST | `/auth/login` | Login local | 10 req/min por IP |
| POST | `/auth/google` | Login con Google ID Token | 10 req/min por IP |
| GET | `/auth/verify-email/{token}` | Confirmar email | — |
| POST | `/auth/forgot-password` | Solicitar reset de contraseña | 3 req/min por IP |
| POST | `/auth/reset-password` | Restablecer contraseña con token | — |
| POST | `/auth/refresh-token` | Rotar tokens | — |

### Requieren JWT

| Método | Ruta | Descripción |
|--------|------|-------------|
| POST | `/auth/change-password` | Cambiar contraseña (invalida todas las sesiones) |
| POST | `/auth/logout` | Cerrar sesión actual |
| POST | `/auth/logout-all` | Cerrar todas las sesiones |
| GET | `/auth/sessions` | Listar sesiones activas |
| POST | `/auth/sessions/revoke/{idSesion}` | Revocar sesión específica |

### Monitoreo

| Método | Ruta | Descripción |
|--------|------|-------------|
| GET | `/health` | Estado de dependencias (PostgreSQL + Redis) |
| GET | `/metrics` | Métricas Prometheus (protegido en producción) |

---

## 🚢 CI/CD y despliegue

**GitHub Actions:**
- `ci.yml`: ejecuta build + todos los tests en cada push o PR a `main`
- `deploy.yml`: despliega a Fly.io automáticamente cuando CI pasa en `main`

**Requisito**: configurar el secret `FLY_API_TOKEN` en el repositorio de GitHub.

```bash
# Generar token de deploy
fly tokens create deploy -x 999999h
```

**Fly.io setup inicial:**

```bash
fly auth login
fly postgres create --name authservice-db --region gru
fly postgres attach authservice-db --app authservice
fly secrets set "Jwt__Key=..." "Email__ResendApiKey=..." "App__BaseUrl=..."
```

---

## 🌟 Autor

Desarrollado por **Claudio Piña**

Microservicio de autenticación production-ready construido sobre .NET 8 + PostgreSQL con arquitectura en capas, tests automatizados y CI/CD.
