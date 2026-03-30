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
- Emisión de Access Tokens (JWT, 15 min)
- Emisión y rotación de Refresh Tokens (7 días)
- Recuperación y reset de contraseña por email
- Cambio de contraseña con cierre forzado de todas las sesiones
- Manejo de sesiones múltiples (por dispositivo/cliente)
- Logout individual y logout global
- Revocación de sesiones específicas
- Validación de sesiones activas en cada request

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
        +---> [ EmailService (Resend SDK) ]
        |
        v
[ Repositories (Npgsql raw SQL) ]
        |
        v
[ PostgreSQL ]
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
- **Logout forzado al cambiar contraseña**: invalida todas las sesiones activas
- **Prevención de enumeración de usuarios**: `forgot-password` siempre retorna la misma respuesta
- **Rate limiting por IP**: límites separados para register, login y forgot-password
- **CORS configurable**: permisivo en Development, restringido en producción
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
| Resend SDK | Envío de emails transaccionales |
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
│   │   └── AuthEndpoints.cs    # Definición de las 11 rutas
│   ├── Middlewares/
│   │   └── ValidarSesionMiddleware.cs
│   ├── Models/                 # Entidades (Usuario, SesionUsuario, etc.)
│   ├── Repositories/           # Acceso a datos (Npgsql raw SQL)
│   ├── Services/
│   │   ├── IAutenticacionService.cs
│   │   ├── AutenticacionService.cs
│   │   ├── IEmailService.cs
│   │   └── EmailService.cs
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

El proyecto tiene 27 tests en total:

| Tipo | Archivo | Tests |
|------|---------|-------|
| Unit | `PasswordPolicyTests.cs` | 7 |
| Unit | `PasswordHasherTests.cs` | 4 |
| Unit | `TokenGeneratorTests.cs` | 5 |
| Unit | `JwtGeneratorTests.cs` | 3 |
| Integration | `AuthIntegrationTests.cs` | 16 |

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

Para probar endpoints protegidos:
1. Registrar usuario: `POST /auth/register`
2. Verificar email con el link del correo (o usar `verificar_url_dev` en la respuesta en Development)
3. Login: `POST /auth/login` → copiar `accessToken`
4. Click en **Authorize** en Swagger → ingresar `Bearer <accessToken>`

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

| Método | Ruta | Rate Limit |
|--------|------|------------|
| POST | `/auth/register` | 5 req/min por IP |
| POST | `/auth/login` | 10 req/min por IP |
| GET | `/auth/verify-email/{token}` | — |
| POST | `/auth/forgot-password` | 3 req/min por IP |
| POST | `/auth/reset-password` | — |
| POST | `/auth/refresh-token` | — |

### Requieren JWT

| Método | Ruta |
|--------|------|
| POST | `/auth/change-password` |
| POST | `/auth/logout` |
| POST | `/auth/logout-all` |
| GET | `/auth/sessions` |
| POST | `/auth/sessions/revoke/{idSesion}` |

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
