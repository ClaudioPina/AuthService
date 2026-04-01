# Informe Técnico: AuthService

**Fecha:** 1 de abril de 2026
**Versión analizada:** rama `main` (commit e7e3605)
**Autor del análisis:** Claude Sonnet 4.6

---

## 1. ¿Qué es AuthService?

AuthService es un **microservicio de autenticación independiente** construido con .NET 8 y ASP.NET Core Minimal APIs. Su propósito es resolver completamente la capa de identidad y acceso de cualquier aplicación que lo consuma, sin acoplarse a ningún dominio de negocio específico.

El servicio está diseñado para ser desplegado como unidad autónoma (actualmente en Fly.io) y consumido por otros servicios o frontends vía HTTP/REST. No tiene interfaz de usuario propia — expone únicamente una API JSON documentada con Swagger.

**Stack tecnológico:**

| Capa | Tecnología |
|------|-----------|
| Framework | .NET 8, ASP.NET Core Minimal APIs |
| Base de datos | PostgreSQL (sin ORM — SQL directo con Npgsql) |
| Autenticación | JWT Bearer (HMAC-SHA256) |
| Hashing de contraseñas | BCrypt.NET (work factor 12) |
| Hashing de tokens | SHA-256 |
| Email transaccional | Resend SDK v0.2.2 |
| Resiliencia DB | Polly (3 reintentos, 300ms delay) |
| Logging | Serilog (console sink) |
| Testing | xUnit + FluentAssertions + Testcontainers |
| CI/CD | GitHub Actions → Fly.io |

---

## 2. Arquitectura

### 2.1 Modelo de autenticación híbrido

AuthService implementa un modelo que combina lo mejor de dos enfoques:

**Stateless (JWT):** Cada request autenticado porta un Access Token firmado (validez 15 min por defecto). El servidor no necesita consultar la base de datos para validar la firma — es verificación criptográfica pura. El token incluye tres claims: `id` (ID del usuario), `email` y `id_sesion`.

**Stateful (Sesiones):** Existe un `ValidarSesionMiddleware` que, en cada request protegido, consulta la tabla `SESIONES_USUARIOS` para verificar que la sesión siga activa. Esto permite **revocación inmediata** — si se hace logout, el JWT queda efectivamente anulado aunque todavía no haya expirado criptográficamente.

Este híbrido resuelve el problema clásico de los sistemas puramente JWT: que un token robado o una sesión que debería cerrarse no se puede invalidar antes de su expiración. El costo es una query adicional por request en rutas protegidas.

### 2.2 Capas

```
AuthEndpoints.cs
    └── AutenticacionService (lógica de negocio)
         ├── EmailService (Resend SDK)
         └── Repositories (Npgsql raw SQL)
              └── AppDbContext (conexiones + Polly retry)
```

Los endpoints solo extraen datos del request y delegan. Toda la lógica de negocio vive en `AutenticacionService`. Los repositorios solo ejecutan SQL — no hay reglas de negocio ahí.

### 2.3 Esquema de base de datos

Cuatro tablas, todas con convención `UPPER_SNAKE_CASE`:

- **USUARIOS** — cuentas con soporte para login local y Google (campo `google_sub`), BCrypt hash, estado de verificación de email.
- **SESIONES_USUARIOS** — sesiones activas con refresh token hasheado (SHA-256), IP de origen, user-agent, y fecha de expiración.
- **VERIFICACION_EMAIL** — tokens temporales de verificación (TTL configurable, default 24h).
- **RESET_PASSWORD** — tokens temporales de recuperación de contraseña (TTL configurable, default 1h).

No se usa Entity Framework — el esquema se gestiona manualmente con `script_DB.sql`.

---

## 3. Capacidades actuales

### 3.1 Registro y verificación de identidad

- **Registro local** (`POST /auth/register`): Valida email, aplica política de contraseña (mín. 8 chars, mayúscula, minúscula, número, símbolo), verifica que el email no esté en uso, hashea la contraseña con BCrypt (work factor 12), y envía un email de verificación con link de tiempo limitado.
- **Verificación de email** (`GET /auth/verify-email/{token}`): Activa la cuenta. Login no es posible hasta verificar.
- En entorno Development, los links de verificación y reset se retornan directamente en la respuesta JSON para facilitar el testing sin servidor de email real.

### 3.2 Autenticación y sesiones

- **Login** (`POST /auth/login`): Retorna par de tokens (Access Token JWT + Refresh Token opaco). Implementa mensaje de error genérico para prevenir enumeración de usuarios. Limita a 4 sesiones activas simultáneas por usuario (revoca las más antiguas al superar el límite).
- **Refresh Token** (`POST /auth/refresh-token`): Implementa **rotación segura** — al usar un refresh token, la sesión anterior se invalida y se crea una nueva. Si alguien intercepta un refresh token y lo usa primero, la sesión del usuario legítimo queda revocada.
- **Logout** (`POST /auth/logout`): Invalida la sesión actual en BD.
- **Logout global** (`POST /auth/logout-all`): Invalida todas las sesiones del usuario.

### 3.3 Gestión de sesiones

- **Listar sesiones** (`GET /auth/sessions`): Devuelve todas las sesiones activas con metadata (user-agent, IP de origen, fecha de creación). El hash del refresh token nunca se expone.
- **Revocar sesión específica** (`POST /auth/sessions/revoke/{idSesion}`): Cierra una sesión concreta (útil para "cerrar sesión en este dispositivo").

### 3.4 Gestión de contraseñas

- **Cambio de contraseña** (`POST /auth/change-password`): Requiere contraseña actual. Al cambiar, **invalida todas las sesiones** del usuario (excepto la actual, implícitamente, ya que ese JWT también quedaría inválido en el próximo ciclo de refresh). Previene que contraseñas anteriores sigan siendo usadas desde dispositivos comprometidos.
- **Recuperación de contraseña** (`POST /auth/forgot-password` + `POST /auth/reset-password`): Flujo estándar por email con token de tiempo limitado (1h). La respuesta de `forgot-password` es siempre la misma, independientemente de si el email existe o no.

### 3.5 Seguridad por capas

| Mecanismo | Implementación |
|-----------|---------------|
| Rate limiting | 5 registros/min, 10 logins/min, 3 forgot-password/min (por IP) |
| Password hashing | BCrypt work factor 12 |
| Token storage | SHA-256 hash en BD (nunca texto plano) |
| Session validation | Middleware consulta BD en cada request protegido |
| User enumeration prevention | Errores genéricos en login y forgot-password |
| Refresh token rotation | Sesión anterior se invalida en cada refresh |
| Forced logout on password change | Todas las sesiones se invalidan |
| Docker security | Contenedor corre con usuario `appuser` sin privilegios root |
| HTTPS | Forzado en Fly.io |

### 3.6 Observabilidad y operaciones

- **Health check** (`GET /health`): Endpoint simple para monitoring.
- **Serilog**: Logging estructurado con request logging automático.
- **Swagger UI**: Disponible en `/swagger` solo en Development, con soporte para autenticación JWT Bearer.
- **CI/CD**: Pipeline completo — cada push a `main` ejecuta build + tests; si pasan, deploy automático a Fly.io.
- **Testcontainers**: 16 tests de integración que levantan PostgreSQL real en Docker, validando el flujo completo de extremo a extremo.

---

## 4. Limitaciones actuales

### 4.1 Funcionales

**Sin OAuth / Login social:** El modelo `Usuario` ya tiene los campos `google_sub` y `foto_url` preparados, pero la funcionalidad de "Continuar con Google" no está implementada. Cualquier integración con OAuth2 (Google, GitHub, etc.) requeriría un trabajo significativo.

**Sin 2FA / MFA:** No hay soporte para autenticación multifactor (TOTP, SMS, email OTP). Para aplicaciones con requisitos de seguridad elevados, esto es una brecha.

**Sin bloqueo por intentos fallidos:** El rate limiting protege por volumen (por IP), pero no hay lógica de account lockout. Un atacante con múltiples IPs puede intentar fuerza bruta sin que la cuenta se bloquee.

**Sin notificaciones de seguridad:** No se notifica al usuario cuando hay un nuevo login desde IP/device desconocido, cuando se cambia la contraseña, o cuando se revocan sesiones.

**Sin roles ni permisos:** AuthService solo maneja identidad — no tiene sistema de autorización (roles, claims de permiso, scopes). Cualquier aplicación consumidora debe implementar su propia lógica de autorización.

### 4.2 Técnicas

**Limpieza de tokens expirados pendiente:** El método `InvalidarTokensExpiradosAsync()` en `ResetPasswordRepository` está implementado pero **nunca se llama**. Los tokens expirados de verificación de email y reset de contraseña acumulan filas en la BD indefinidamente. No hay scheduled task ni cron job para limpieza periódica.

**Sesiones expiradas no se limpian:** Similar al punto anterior — las sesiones con `expira_en` pasada permanecen en `SESIONES_USUARIOS` con `estado = 1`. El middleware las validaría si alguien presentara el token, pero nunca se eliminan proactivamente.

**Sin validación de formato de email:** Solo se verifica que el campo no sea nulo/vacío. No hay validación RFC 5322 del formato. Un email como `abc@` pasaría la validación inicial (fallaría en el intento de envío).

**Sin cache:** Cada request autenticado hace al menos una query a BD (validación de sesión). Sin Redis u otro cache, la latencia de BD impacta directamente en el tiempo de respuesta de cada endpoint protegido.

**Solo un proveedor de email:** La abstracción `IEmailService` permite intercambiar el proveedor, pero solo existe la implementación con Resend. Si Resend falla o se decide migrar a SendGrid/AWS SES, habría que implementar desde cero.

**Configuración parcialmente hardcodeada:** El límite de 4 sesiones activas por usuario está hardcodeado en `AutenticacionService`. Aunque los TTL de tokens son configurables via appsettings, este límite no lo es.

**Auditoría de cambios incompleta:** El modelo `Usuario` tiene campos `UsuarioAud` y `Actualizacion`, pero ningún update en los repositorios los actualiza. No hay registro de quién modificó qué y cuándo.

**Sin soporte multi-tenant real:** Hay una columna `propietario` en el schema que es remanente de una arquitectura anterior. No está activa en el código actual, pero genera confusión en el schema.

### 4.3 Operacionales

**Auto-suspend en Fly.io:** La configuración `auto_stop_machines = 'stop'` y `min_machines_running = 0` hace que la máquina se suspenda por inactividad. El primer request tras un período idle tiene cold start latency (puede ser 3-10 segundos).

**Sin HA (High Availability):** Una sola máquina en una sola región (GRU). Caída de la máquina = servicio no disponible.

**Sin métricas de negocio:** No hay instrumentación para métricas como tasa de registros exitosos, tasa de logins fallidos, tokens expirados vs. usados, etc.

---

## 5. Mejoras propuestas

Las mejoras están ordenadas por impacto/esfuerzo estimado, priorizando las más críticas.

### 5.1 Prioridad alta — Correcciones y deuda técnica

**Scheduled cleanup de tokens expirados**
El método ya existe. Solo falta invocarlo. La solución más simple es un `IHostedService` que ejecute la limpieza cada hora:

```csharp
// CleanupExpiredTokensService.cs
public class CleanupExpiredTokensService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _resetPasswordRepo.InvalidarTokensExpiradosAsync();
            await _verificacionEmailRepo.InvalidarTokensExpiradosAsync();
            await _sesionesRepo.InvalidarSesionesExpiradasAsync(); // nuevo método
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
```

**Validación de formato de email**
Agregar validación RFC 5322 antes de intentar cualquier operación. .NET tiene `MailAddress` para esto:

```csharp
private static bool EsEmailValido(string email)
{
    try { _ = new System.Net.Mail.MailAddress(email); return true; }
    catch { return false; }
}
```

**Actualizar campo `Actualizacion` en updates**
Cada `UPDATE` en los repositorios debería incluir `SET actualizacion = NOW()` para tener trazabilidad básica de cambios.

### 5.2 Prioridad media — Seguridad

**Account lockout temporal por intentos fallidos**
Implementar bloqueo temporal (ej. 15 minutos) después de N intentos fallidos desde una misma IP o para un mismo email. Puede implementarse con una tabla `INTENTOS_LOGIN` o con Redis si está disponible.

**Notificaciones de seguridad por email**
Enviar email al usuario cuando:
- Se hace login desde un nuevo dispositivo/IP
- Se cambia la contraseña
- Se activa el login desde muchos dispositivos

**Política de contraseña mejorada**
La validación actual con `[\W_]` para símbolos puede ser confusa. Una lista explícita de caracteres permitidos mejora la UX:

```csharp
private static readonly char[] SpecialChars = "!@#$%^&*()-_=+[]{}|;:',.<>?/`~".ToCharArray();
```

**Revocación de todas las sesiones al detectar refresh token reutilizado**
Si un refresh token ya invalidado es presentado (posible señal de robo), invalidar **todas** las sesiones del usuario como medida de contención automática. Actualmente solo falla silenciosamente.

### 5.3 Prioridad media — Funcionalidad

**Login con Google (OAuth 2.0)**
El schema ya tiene `google_sub`. El trabajo técnico involucra:
1. Validar el ID Token de Google (librería `Google.Apis.Auth`)
2. Crear/buscar usuario por `google_sub`
3. Retornar el mismo par de tokens JWT + Refresh que el login local

**Soporte para múltiples proveedores de email**
Crear implementaciones adicionales de `IEmailService`:
- `SendGridEmailService`
- `SmtpEmailService` (para desarrollo local sin API externa)

La selección del proveedor por configuración en appsettings.

**Limit de sesiones configurable**
Mover el `maxSesiones = 4` a appsettings:

```json
"Sesiones": {
  "MaxActivasPorUsuario": 4
}
```

### 5.4 Prioridad baja — Observabilidad y operaciones

**Métricas con OpenTelemetry**
Instrumentar contadores clave:
- `auth.registrations.total` (éxitos/fallos)
- `auth.logins.total` (éxitos/fallos)
- `auth.sessions.active` (gauge)
- `auth.tokens.refreshed`

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m.AddAspNetCoreInstrumentation());
```

**Cache de sesiones con Redis**
Para reducir queries de BD por el `ValidarSesionMiddleware`:

```csharp
// Cache sesión por 5 minutos (el JWT expira en 15)
// Invalidación explícita en logout/revoke
await _cache.SetAsync($"session:{idSesion}", sessionData, TimeSpan.FromMinutes(5));
```

Esto reduciría el costo de la query de validación de sesión de una round-trip a BD a un hit de cache en memoria.

**Alertas de Fly.io / uptime monitoring**
Configurar un monitor externo (Uptime Robot, Better Uptime, etc.) que haga ping a `/health` y alerte si el servicio no responde en < 30 segundos (considerando cold start).

**2FA con TOTP**
Agregar soporte para autenticadores (Google Authenticator, Authy) usando la librería `OtpNet`:
1. Generar secret TOTP al activar 2FA
2. Verificar código de 6 dígitos en login
3. Almacenar secret cifrado en USUARIOS

---

## 6. Resumen ejecutivo

AuthService es un microservicio funcional y correctamente estructurado que implementa los flujos de autenticación más comunes con buenas prácticas de seguridad: BCrypt para contraseñas, SHA-256 para tokens en BD, rotación de refresh tokens, prevención de enumeración de usuarios, y validación de sesiones en tiempo real.

Las mayores fortalezas son la solidez de los mecanismos de seguridad implementados y la cobertura de tests de integración (16 tests end-to-end con PostgreSQL real). La arquitectura en capas está limpia y es fácil de extender.

Las brechas más relevantes para producción real son: la acumulación de registros expirados en BD (sin cleanup), la ausencia de 2FA, la falta de bloqueo por intentos fallidos, y el cold start en Fly.io si se mantiene la configuración actual de auto-suspend.

Para la mayoría de aplicaciones de uso personal o proyectos pequeños/medianos, AuthService está en un estado production-ready. Para aplicaciones con requisitos de seguridad elevados (fintech, salud, datos sensibles), las mejoras de prioridad alta y media serían necesarias antes de considerarlo completo.

---

*Informe generado mediante análisis estático del código fuente. Último commit analizado: `e7e3605` (01/04/2026).*
