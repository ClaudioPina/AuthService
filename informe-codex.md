# Registro de cambios — AuthService

Documento de seguimiento de hallazgos, implementaciones y deuda técnica pendiente.
Fuentes: análisis Codex + análisis complementario Claude (abril 2026).

Contexto de consumo: una instancia de AuthService por app, solo apps propias Vue 2 + .NET.
HS256 con clave compartida es suficiente — no se necesita RS256 ni JWKS ni OAuth 2.0.

---

## Implementado / Modificado

### Bloque 1 — Seguridad crítica

#### 1.1 Forwarded headers para IPs correctas en Fly.io
El rate limiting y las IPs registradas en sesiones/auditoría usaban la IP del proxy
en lugar de la del cliente real.

**Solución aplicada:**
- `Program.cs` — agregado `UseForwardedHeaders` con `X-Forwarded-For` y `X-Forwarded-Proto`
  antes de `UseHttpsRedirection`.

---

#### 1.2 Account lockout: UPSERT correcto con índice único
Cada intento fallido podía generar múltiples filas para el mismo email.
El `ON CONFLICT DO NOTHING` no generaba conflicto porque no había constraint único real.

**Solución aplicada:**
- `script_DB.sql` — eliminado índice simple, reemplazado por `CREATE UNIQUE INDEX IDX_INTENTOS_EMAIL ON INTENTOS_LOGIN (LOWER(email))`.
- `Repositories/IntentosLoginRepository.cs` — reescrito `RegistrarIntentoFallidoAsync` como
  un UPSERT real: `INSERT ... ON CONFLICT (LOWER(email)) DO UPDATE SET intentos = intentos + 1, ...`

---

#### 1.3 Tokens de verificación y reset hasheados en BD
Los tokens se guardaban en texto plano. Con acceso de lectura a la BD, eran utilizables directamente.

**Solución aplicada:**
- `Services/AutenticacionService.cs` — en `RegisterAsync` y `ForgotPasswordAsync`:
  generar token plano → `TokenGenerator.HashToken(token)` → almacenar hash → enviar plano en el link.
- En `VerifyEmailAsync` y `ResetPasswordAsync`: hashear el token recibido antes de buscar en BD.
- Los repositorios (`VerificacionEmailRepository`, `ResetPasswordRepository`) no cambiaron —
  solo almacenan/buscan el string que reciben. El control del hashing está en el servicio.

---

#### 1.4 Reset de contraseña invalida sesiones y notifica al usuario
Después de resetear la contraseña, las sesiones activas del usuario seguían válidas.

**Solución aplicada:**
- `Services/AutenticacionService.cs` — `ResetPasswordAsync` ahora:
  1. Obtiene sesiones activas del usuario.
  2. Limpia cache de cada sesión (`RemoveSesionCacheAsync`).
  3. Invalida todas las sesiones en BD.
  4. Envía notificación de seguridad por email (fire-and-forget).

---

#### 1.5 Transacciones en operaciones críticas multi-paso
Un fallo a mitad de flujo podía dejar estados inconsistentes
(token marcado pero password no actualizado, sesión invalidada pero nueva no creada, etc.).

**Solución aplicada:**
- `Data/AppDbContext.cs` — nuevo método `BeginTransactionAsync()` que retorna
  `(NpgsqlConnection, NpgsqlTransaction)` compartibles entre repositorios.
- `Repositories/SesionesUsuariosRepository.cs` — overloads de `InvalidarSesionPorHashAsync`
  y `CrearSesionAsync` que aceptan conexión y transacción externas.
- `Repositories/UsuariosRepository.cs` — overload de `ActualizarPasswordAsync`
  con conexión y transacción externas.
- `Repositories/ResetPasswordRepository.cs` — overload de `MarcarTokenComoUsadoAsync`
  con conexión y transacción externas.
- `Services/AutenticacionService.cs` — `ResetPasswordAsync` y `RefreshTokenAsync`
  usan transacción explícita con try/catch/rollback/finally.

---

### Bloque 2 — Robustez y errores controlados

#### 2.1 Login de usuario Google-only: error controlado (no 500)
Un usuario creado exclusivamente con Google tiene `password_hash = NULL`.
Intentar comparar esa contraseña lanzaba excepción en lugar de responder un error claro.

**Solución aplicada:**
- `Models/Usuario.cs` — `PasswordHash`, `Nombre`, `FotoUrl`, `ProveedorLogin`, `GoogleSub`
  marcados como `string?` (nullable). `Email` marcado como `required string`.
- `Repositories/UsuariosRepository.cs` — `reader.GetString(3)` de `password_hash` reemplazado
  por `reader.IsDBNull(3) ? null : reader.GetString(3)`.
- `Services/AutenticacionService.cs` — `LoginAsync` verifica `usuario.PasswordHash == null`
  y retorna error 400 con mensaje específico antes de intentar comparar.
- `Services/AutenticacionService.cs` — `ChangePasswordAsync` idem: detecta cuenta Google-only
  y retorna error controlado.

---

#### 2.2 Handler global de errores: mensaje genérico en producción
El handler exponía `ex.Message` al cliente en errores 500, filtrando detalles internos
(stack traces, mensajes de Npgsql, nombres de tablas).

**Solución aplicada:**
- `Program.cs` — `UseExceptionHandler` retorna mensaje genérico en producción:
  `"Ocurrió un error inesperado. Por favor, intenta nuevamente más tarde."`
  En `Development` sigue mostrando el mensaje real para facilitar debug local.

---

#### 2.3 Límite de sesiones limpia cache de sesiones desactivadas
Cuando `LimitarSesionesActivasAsync` desactivaba sesiones antiguas por límite configurable,
no limpiaba su cache. Esas sesiones seguían siendo aceptadas durante hasta 5 minutos.

**Solución aplicada:**
- `Repositories/SesionesUsuariosRepository.cs` — `LimitarSesionesActivasAsync` cambia tipo
  de retorno a `Task<List<long>>` y agrega `RETURNING id_sesion` al UPDATE.
- `Services/AutenticacionService.cs` — `LoginAsync` y `GoogleLoginAsync` iteran los IDs
  retornados y llaman `RemoveSesionCacheAsync` por cada uno.

---

#### 2.4 Validación de configuración al arranque (fail-fast)
Una configuración faltante podía romper la app tarde, en runtime, con mensajes crípticos.

**Solución aplicada:**
- `Program.cs` — bloque de validación después de `builder.Build()` que verifica:
  `Jwt:Key` (mínimo 32 caracteres), `App:BaseUrl`, `Email:FromAddress`, `Google:ClientId`,
  y conexión a BD (`DATABASE_URL` o `ConnectionStrings:PostgresDb`).
  Si falta alguno: `Log.Fatal(...)` + `Environment.Exit(1)`.

---

### Bloque 3 — Calidad y consistencia

#### 3.1 Email normalizado antes de persistir
`User@Mail.COM` y `user@mail.com` podían coexistir como cuentas distintas.

**Solución aplicada:**
- `Services/AutenticacionService.cs` — `RegisterAsync`, `LoginAsync` y `ForgotPasswordAsync`
  aplican `.Trim().ToLowerInvariant()` al email antes de cualquier operación.

---

#### 3.2 Índice único case-insensitive para email en BD
El `UNIQUE` sobre `email` era case-sensitive. Se podían registrar dos cuentas con el mismo
email en diferente case a nivel de BD.

**Solución aplicada:**
- `script_DB.sql` — eliminado `UNIQUE` inline de la columna `email` en `USUARIOS`.
  Reemplazado por `CREATE UNIQUE INDEX IDX_USUARIOS_EMAIL ON USUARIOS (LOWER(email))`.
- **Script manual a ejecutar en BD existente:**
  ```sql
  ALTER TABLE USUARIOS DROP CONSTRAINT IF EXISTS usuarios_email_key;
  CREATE UNIQUE INDEX IDX_USUARIOS_EMAIL ON USUARIOS (LOWER(email));
  ```

---

#### 3.3 Security headers HTTP
Faltaban headers que los navegadores esperan para prevenir clickjacking y MIME sniffing.

**Solución aplicada:**
- `Program.cs` — middleware que agrega en cada respuesta:
  `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`.

---

#### 3.4 Correlation ID en logs
No había forma de correlacionar todos los logs de una misma request en producción.

**Solución aplicada:**
- `Program.cs` — middleware (primero en el pipeline) que propaga `X-Correlation-Id`
  desde el header del cliente o genera uno nuevo (`Guid.NewGuid().ToString("N")`).
  Inyectado en Serilog via `LogContext.PushProperty("CorrelationId", ...)`.
  El ID se devuelve también en el header de la respuesta.

---

#### 3.5 HTML encoding en plantillas de email
`userAgent` e `ip` se interpolaban directamente en HTML sin sanitización.

**Solución aplicada:**
- `Services/EmailService.cs` — `WebUtility.HtmlEncode(ip)` y `WebUtility.HtmlEncode(userAgent)`
  en `SendNewLoginNotificationAsync`.
- `Services/SmtpEmailService.cs` — mismo fix.

---

#### 3.6 Contratos de respuesta consistentes
`login` y `google` respondían con una estructura de tokens; `refresh-token` con otra distinta.

**Solución aplicada:**
- `Services/AutenticacionService.cs` — `RefreshTokenAsync` ahora responde con la misma
  estructura que login: `{ tokens: { accessToken, accessTokenExpiresInMinutes, refreshToken, refreshTokenExpiresAt } }`.
- `AuthIntegrationTests.cs` — test `RefreshToken_WithValidToken_ShouldReturnNewTokens`
  actualizado para usar la nueva estructura.

---

#### 3.7 Validación de entradas en ChangePasswordAsync
`ChangePasswordRequest` usaba `null!` y el método no validaba nulos/vacíos.

**Solución aplicada:**
- `Services/AutenticacionService.cs` — `ChangePasswordAsync` valida `string.IsNullOrWhiteSpace`
  en ambas contraseñas antes de continuar.

---

### Bloque 4 — Observabilidad y tests

#### 4.1 Health checks reales para PostgreSQL y Redis
`AddHealthChecks()` sin checks reales no detectaba dependencias caídas.

**Solución aplicada:**
- `HealthChecks/PostgresHealthCheck.cs` (nuevo) — ejecuta `SELECT 1` via `AppDbContext`.
- `HealthChecks/RedisHealthCheck.cs` (nuevo) — verifica round-trip de write/read en cache.
  Si Redis no está configurado, retorna `Healthy` con nota informativa.
- `Program.cs` — `.AddCheck<PostgresHealthCheck>("postgres").AddCheck<RedisHealthCheck>("redis")`.
- `Program.cs` — endpoint `GET /health` responde JSON detallado con estado de cada dependencia.
- Sin nuevas dependencias NuGet — implementación custom.

---

#### 4.2 Protección del endpoint /metrics
`GET /metrics` (Prometheus) estaba expuesto públicamente sin autenticación.

**Solución aplicada:**
- `Program.cs` — middleware que intercepta `/metrics`: si `Metrics:ApiKey` está configurado,
  exige header `X-Metrics-Token` con ese valor. Si está vacío, el endpoint es público
  (útil en desarrollo local).
- `appsettings.example.json` — agregada sección `"Metrics": { "ApiKey": "" }`.

---

#### 4.3 Audit log de operaciones sensibles
No había tabla de auditoría. Sin ella, no hay trazabilidad de quién hizo qué, cuándo y desde dónde.

**Solución aplicada:**
- `script_DB.sql` — nueva tabla `AUDITORIA (id_auditoria, usuario_id, accion, ip, user_agent, timestamp)`
  con índices en `usuario_id` y `timestamp`.
- `Repositories/AuditoriaRepository.cs` (nuevo) — método `RegistrarAsync` con INSERT.
- `Services/AutenticacionService.cs` — llamadas fire-and-forget (no bloquean el flujo) en:
  - `LoginAsync` → `"LOGIN"`
  - `ResetPasswordAsync` → `"RESET_CONTRASENA"`
  - `ChangePasswordAsync` → `"CAMBIO_CONTRASENA"`
  - `LogoutAllAsync` → `"LOGOUT_ALL"`
  - `RevokeSessionAsync` → `"REVOCACION_SESION"`
- `Program.cs` — `AuditoriaRepository` registrado como scoped en DI.

---

#### 4.4 Tests de integración para flujos no cubiertos
Varios flujos críticos de seguridad no tenían cobertura de tests.

**Solución aplicada** — 6 tests nuevos en `AuthService.Tests/Integration/AuthIntegrationTests.cs`:
1. `Login_AfterMaxFailedAttempts_ShouldReturnLockedError` — lockout tras N intentos fallidos.
2. `ChangePassword_ShouldInvalidateAllActiveSessions` — verifica que las otras sesiones se revocan.
3. `LogoutAll_ShouldInvalidateAllSessions` — verifica que todas las sesiones quedan inválidas.
4. `RevokeSession_ShouldInvalidateOnlyThatSession` — revoca una sesión específica y verifica que la otra sigue activa.
5. `RefreshToken_ReuseDetected_ShouldRevokeAllSessions` — reutilizar un refresh token rotado revoca todo.
6. `ResetPassword_ShouldInvalidateAllActiveSessions` — sesiones activas quedan inválidas tras reset.

`AuthWebAppFactory.cs` — agregado `Lockout:MaxIntentos = 3` para que el test de lockout sea rápido.

**Total de tests:** 41 (19 unitarios + 22 de integración). Todos pasan.

---

### Bloque 5 — Funcionalidades adicionales

#### P3 — Endpoint `/auth/me`

**Solución aplicada:**
- `Repositories/UsuariosRepository.cs` — extendida query de `ObtenerUsuarioPorIdAsync` para incluir `foto_url`, `proveedor_login`, `creacion`.
- `DTOs/Auth/PerfilUsuarioResponse.cs` (nuevo) — DTO con `id`, `email`, `nombre`, `foto_url`, `email_verificado`, `proveedor_login`, `creacion`.
- `Services/IAutenticacionService.cs` + `AutenticacionService.cs` — nuevo método `ObtenerPerfilAsync`.
- `Endpoints/AuthEndpoints.cs` — `GET /auth/me` con `RequireAuthorization()`.

---

#### P2 — HaveIBeenPwned

**Solución aplicada:**
- `Services/IHibpService.cs` + `HibpService.cs` (nuevos) — implementación k-anonymity con SHA-1 prefix. Timeout 3s, fail open si HIBP no responde.
- `Program.cs` — `AddHttpClient<IHibpService, HibpService>` con `User-Agent: AuthService/1.0`.
- `Services/AutenticacionService.cs` — check integrado en `RegisterAsync` y `ChangePasswordAsync` después de `PasswordPolicy.Validate`.
- `Tests/Integration/FakeHibpService.cs` (nuevo) — siempre retorna `false`. Reemplaza `HibpService` real en `AuthWebAppFactory`.

---

#### P5 — Endpoint de reenvío de verificación de email

**Solución aplicada:**
- `Repositories/VerificacionEmailRepository.cs` — nuevo método `InvalidarTokensAnterioresAsync`.
- `DTOs/Auth/ResendVerificationRequest.cs` (nuevo) — DTO con `Email`.
- `Services/IAutenticacionService.cs` + `AutenticacionService.cs` — nuevo método `ResendVerificationAsync`. Respuesta idéntica para evitar enumeración de usuarios.
- `Program.cs` — política de rate limit `resendverification-policy` (3 req/min).
- `Endpoints/AuthEndpoints.cs` — `POST /auth/resend-verification`.

---

#### P7 — Tests de health checks con dependencias caídas

**Solución aplicada:**
- `Tests/Integration/HealthCheckTests.cs` (nuevo) — 3 tests: PostgreSQL disponible → Healthy, Redis no configurado → Healthy con nota, PostgreSQL caído → test directo de `PostgresHealthCheck` con `AppDbContext` de conexión inválida (sin factory extra).
- `Tests/Integration/AuthIntegrationCollection.cs` (nuevo) — colección xUnit compartida. Resuelve el error "Serilog logger already frozen" que ocurre cuando múltiples clases usan `IClassFixture<AuthWebAppFactory>` por separado.

---

#### P6 — Tests de concurrencia para lockout

**Solución aplicada:**
- `Tests/Integration/LockoutConcurrencyTests.cs` (nuevo) — 3 tests con `Task.WhenAll`:
  1. `ConcurrentFailedLogins_ShouldTriggerLockout` — N intentos concurrentes activan el bloqueo.
  2. `ConcurrentFailedLogins_ShouldNotCreateDuplicateRows` — verifica 1 sola fila en `INTENTOS_LOGIN` vía query directa.
  3. `ConcurrentFailedLogins_CounterShouldBeAccurate` — contador exactamente igual a N intentos registrados.

**Total de tests:** 47 (19 unitarios + 28 de integración). Todos pasan.

---

## Falta por implementar

### Baja prioridad / nice to have

#### P1 — 2FA / TOTP
Agregar autenticación de dos factores via app autenticadora (Google Authenticator, Authy).

Lo que requeriría:
- Paquete `Otp.NET` (generación/validación de códigos TOTP).
- Nuevas columnas en `USUARIOS`: `totp_secret` (cifrado AES-256), `totp_habilitado`.
- 4 endpoints nuevos: `POST /auth/2fa/enable`, `POST /auth/2fa/verify`, `POST /auth/2fa/disable`, `POST /auth/2fa/login`.
- Flujo de login modificado: si 2FA está activo, login retorna un `temp_token` en vez de tokens
  reales. El cliente debe completar el segundo factor con ese token temporal.
- Implementar cuando haya un frontend que lo consuma.

---

#### P4 — API versioning `/v1/`
Rutas `/v1/auth/*` para compatibilidad futura cuando haya clientes externos.

Lo que requeriría:
- Cambio en `AuthEndpoints.cs`: `app.MapGroup("/v1/auth")`.
- Decisión sobre si mantener `/auth/*` como alias o hacer redirect.
- Actualmente no es necesario porque solo hay apps propias — puede esperar hasta
  que haya clientes externos que necesiten estabilidad de contrato.

---

