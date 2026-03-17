
# 🔐 AuthService – Microservicio de Autenticación

**.NET 8 + JWT + Refresh Tokens + PostgreSQL**

AuthService es un microservicio independiente desarrollado en **.NET 8** y responsable exclusivamente de la autenticación, autorización y gestión de sesiones de usuarios.

Está diseñado para ser consumido por:

- Aplicaciones web (SPA)
- Aplicaciones móviles
- Otros microservicios
- APIs internas

**👉 No contiene lógica de negocio (gastos, productos, órdenes, etc.).**
**👉 Su única responsabilidad es identidad y seguridad.**

### Tabla de contenidos

- [🔐 AuthService – Microservicio de Autenticación](#-authservice--microservicio-de-autenticación)
    - [Tabla de contenidos](#tabla-de-contenidos)
  - [🎯 Responsabilidades del microservicio](#-responsabilidades-del-microservicio)
  - [🧱 Arquitectura general](#-arquitectura-general)
  - [🔐 Modelo de autenticación (híbrido)](#-modelo-de-autenticación-híbrido)
      - [AuthService utiliza un modelo híbrido:](#authservice-utiliza-un-modelo-híbrido)
      - [¿Por qué?](#por-qué)
  - [🔁 Flujo de Login](#-flujo-de-login)
  - [🔄 Flujo de Refresh Token](#-flujo-de-refresh-token)
  - [🔒 Seguridad de contraseñas](#-seguridad-de-contraseñas)
  - [🧠 Validación de sesión (Middleware)](#-validación-de-sesión-middleware)
      - [Cada request autenticado pasa por un middleware que:](#cada-request-autenticado-pasa-por-un-middleware-que)
      - [Esto permite:](#esto-permite)
  - [🚀 Tecnologías utilizadas](#-tecnologías-utilizadas)
  - [📦 Dependencias necesarias](#-dependencias-necesarias)
    - [Instalación por consola](#instalación-por-consola)
  - [🗂️ Estructura del proyecto](#️-estructura-del-proyecto)
  - [🔐 Configuración de Oracle Wallet (OBLIGATORIA)](#-configuración-de-oracle-wallet-obligatoria)
  - [⚙️ Configuración del archivo `appsettings.json`](#️-configuración-del-archivo-appsettingsjson)
  - [▶️ Cómo ejecutar el servicio y probar en Swagger](#️-cómo-ejecutar-el-servicio-y-probar-en-swagger)
  - [🔥 Endpoints implementados](#-endpoints-implementados)
    - [🔓 Público](#-público)
    - [🔐 Requiere JWT](#-requiere-jwt)
  - [🧭 Roadmap de mejoras futuras](#-roadmap-de-mejoras-futuras)
  - [🌟 Autor](#-autor)

---

## 🎯 Responsabilidades del microservicio

**AuthService se encarga de:**

- Registro de usuarios
- Verificación de email
- Login con credenciales
- Recuperación de contraseña
- Emisión de Access Tokens (JWT)
- Emisión y rotación de Refresh Tokens
- Manejo de sesiones múltiples
- Logout individual
- Logout global
- Revocación de sesiones específicas
- Cambio de contraseña con cierre forzado de sesión
- Validación de sesiones activas

**❌ Lo que AuthService NO hace:**

- No maneja lógica de gastos
- No almacena datos de negocio
- No gestiona permisos específicos del dominio
- No renderiza vistas
- No depende de otros microservicios

Esto garantiza:
- Bajo acoplamiento
- Alta reutilización
- Escalabilidad


Toda la información se gestiona mediante una base de datos **PostgreSQL**.

---

## 🧱 Arquitectura general

```text
[ Cliente / Frontend ]
        |
        v
[ AuthService API ]
        |
        v
[ PostgreSQL DB ]
```

- JWT → autenticación stateless
- Sesiones en BD → control stateful

---

## 🔐 Modelo de autenticación (híbrido)

#### AuthService utiliza un modelo híbrido:

- Stateless → JWT (Access Token)
- Stateful → Sesiones persistidas en BD

#### ¿Por qué?
- Permite revocar sesiones
- Permite logout global
- Permite cerrar sesión al cambiar contraseña
- Evita JWTs “eternos”

---

## 🔁 Flujo de Login
```text
Usuario
  |
  |  email + password
  v
/auth/login
  |
  |-- valida credenciales
  |-- crea sesión en BD
  |-- genera access_token (JWT)
  |-- genera refresh_token
  v
Cliente recibe tokens
```

---

## 🔄 Flujo de Refresh Token

```text
Cliente
  |
  | refresh_token
  v
/auth/refresh-token
  |
  |-- valida sesión
  |-- invalida sesión anterior
  |-- crea nueva sesión
  |-- emite nuevos tokens
  v
Cliente recibe nuevos tokens
```

---

## 🔒 Seguridad de contraseñas

- Hashing con BCrypt
- Política mínima:
  - ≥ 8 caracteres
  - ≥ 1 mayúscula
  - ≥ 1 minúscula
  - ≥ 1 número
  - ≥ 1 símbolo
- Tokens sensibles nunca se almacenan en texto plano

---

## 🧠 Validación de sesión (Middleware)

#### Cada request autenticado pasa por un middleware que:
1. Extrae sesion_id del JWT
2. Consulta la sesión en BD
3. Verifica que esté activa
4. Bloquea la request si la sesión fue revocada

#### Esto permite:
- Expulsar usuarios tras cambio de contraseña
- Invalidar JWTs antiguos
- Control centralizado de sesiones

---

## 🚀 Tecnologías utilizadas

- **.NET 8 Minimal API**
- **PostgreSQL**
- **Npgsql**
- **JWT (JSON Web Tokens)**
- **BCrypt para hashing de contraseñas**
- **Polly (reintentos automáticos de conexión a BD)**
- **Swagger/OpenAPI**
- **C# 12**
- **Arquitectura de Microservicio Independiente**

---

## 📦 Dependencias necesarias

Estos paquetes deben estar instalados en el proyecto:

```xml
<PackageReference Include="BCrypt.Net-Next" Version="4.0.3" />
<PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="8.0.0" />
<PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="8.0.22" />
<PackageReference Include="Npgsql" Version="8.0.2" />
<PackageReference Include="Polly" Version="8.6.5" />
<PackageReference Include="Swashbuckle.AspNetCore" Version="6.6.2" />
<PackageReference Include="System.IdentityModel.Tokens.Jwt" Version="8.15.0" />
```

### Instalación por consola

```bash
dotnet add package BCrypt.Net-Next
dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer
dotnet add package Microsoft.AspNetCore.OpenApi
dotnet add package Npgsql
dotnet add package Polly
dotnet add package Swashbuckle.AspNetCore
dotnet add package System.IdentityModel.Tokens.Jwt
```

---

## 🗂️ Estructura del proyecto

```text
AuthService.Api/
│
├── Data/
│   └── AppDbContext.cs
│
├── Repositories/
│   ├── UsuariosRepository.cs
│   ├── SesionesUsuariosRepository.cs
│   ├── VerificacionEmailRepository.cs
│   └── ResetPasswordRepository.cs
│
├── Middlewares/
│   └── ValidarSesionMiddleware.cs
│
├── Dtos/
│   └── Auth/
│
├── Utils/
│   ├── PasswordHasher.cs
│   ├── PasswordPolicy.cs
│   ├── TokenGenerator.cs
│   └── JwtGenerator.cs
│
├── Program.cs
└── README.md

```

---

## ⚙️ Configuración del archivo `appsettings.json`

> ⚠️ **Este archivo NO debe subirse a GitHub.**

```json
{
  "Jwt": {
    "Key": "CLAVE_SECRETA_DE_256_BITS",
    "Issuer": "AuthService",
    "Audience": "AuthServiceClients"
  },
  "ConnectionStrings": {
    "PostgresDb": "Host=localhost;Database=authdb;Username=postgres;Password=TU_PASSWORD"
  }
}
```

---

## ▶️ Cómo ejecutar el servicio y probar en Swagger

```bash
dotnet run
```

1. Luego entrar a:
   - https://localhost:5091/swagger
2. Registrar un nuevo usuario
3. Ejecutar `/auth/login`
4. Copiar el `accessToken`
5. Hacer clic en el botón **Authorize**:

  ```
  Bearer <accessToken>
  ```

Luego podrás usar endpoints que requieren autenticación.

---

## 🔥 Endpoints implementados

### 🔓 Público

```http
POST /auth/register
POST /auth/login
GET /auth/verify-email/{token}
POST /auth/forgot-password
POST /auth/reset-password
POST /auth/refresh-token
```

### 🔐 Requiere JWT

```http
POST /auth/change-password
POST /auth/logout
POST /auth/logout-all
GET /auth/sessions
POST /auth/sessions/revoke/{idSesion}
```


## 🧭 Roadmap de mejoras futuras

- Integración con OAuth/Gmail login
- Multi-factor authentication (MFA)
- Roles y permisos avanzados
- Logging estructurado con Serilog
- API Gateway + Load balancing

---

## 🌟 Autor

Desarrollado por **Claudio Piña**

Microservicio diseñado como base sólida para autenticación moderna con .NET + PostgreSQL.