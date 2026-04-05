# TODO tecnico (revision exhaustiva - 2026-04-03)

Este backlog reemplaza el registro historico anterior.  
Enfocado solo en pendientes reales detectados al revisar codigo, tests, UI, CI/CD y documentacion.

## P0 - Critico (hacer primero)

- [x] Limpiar archivos generados versionados en Git (`artifacts/`, `bin/`, `obj/`):
  - 360 archivos sacados del versionado con `git rm --cached`. `.gitignore` actualizado con `artifacts/`.

- [x] Resolver estado de `informe-codex.md` (archivo trackeado eliminado localmente):
  - Eliminado del versionado con `git rm`. Contenido ya reflejado en código y TODO.md.

- [x] Crear `appsettings.example.json` real y mantenerlo como unica plantilla oficial:
  - Creado en `AuthService.Api/appsettings.example.json` con todos los campos documentados.

- [x] Alinear documentacion con el comportamiento actual (README.md + CLAUDE.md):
  - Documentado `GET /auth/confirm-change-password/{token}` en README y CLAUDE.md.
  - `change-password` documentado como flujo con confirmacion por email.
  - Eliminado `X-XSS-Protection` (no implementado en `Program.cs`).
  - Conteo de rutas corregido a 15 en CLAUDE.md.

- [x] Endurecer validacion de configuracion (fail-fast) segun feature activa:
  - `Program.cs`: validacion condicional por `Email:Provider`. `Google:ClientId` opcional.

- [x] Revisar estrategia del token de confirmacion de cambio de password:
  - Migrado de cache distribuida a tabla `RESET_PASSWORD` con columna `tipo = 'change_confirm'`.
  - Agrega columna `nuevo_password_hash` para el hash pre-computado. Robusto en multi-instancia.

## P1 - Alta prioridad

- [x] Agregar doble campo de nueva contrasena en reset password (`/auth/reset-password`):
  - Backend: DTO + validacion `newPassword == confirmacion`.
  - Frontend test-ui: formulario y validacion local.

- [ ] Reducir fuga de informacion en login de cuentas Google-only:
  - Actualmente devuelve mensaje especifico ("cuenta vinculada a Google"), lo que ayuda a enumerar cuentas.
  - Definir modo estricto (mensaje generico) o dejarlo configurable.

- [ ] Completar auditoria con `ip` y `user_agent` en eventos sensibles:
  - Hay acciones que se registran con `null` en esos campos.
  - Pasar contexto desde endpoints para trazabilidad completa.

- [x] Robustecer `ValidarSesionMiddleware` frente a claim `id_sesion` invalido:
  - `long.Parse` reemplazado por `TryParse` con respuesta `401` controlada.

- [x] Revisar configuracion de `UseForwardedHeaders` para Fly/proxy real:
  - En producción se limpian `KnownNetworks`/`KnownProxies` para confiar en el proxy de Fly.io.

- [ ] Investigar por que `dotnet build AuthService.sln` falla localmente sin errores visibles:
  - Compilar proyectos individuales (`.csproj`) si funciona.
  - Solucionar para mantener flujo de build consistente.

## P1 - Cobertura de pruebas faltante

- [x] Agregar integration tests para endpoints y flujos no cubiertos:
  - `/auth/google` (token invalido → 400).
  - `/auth/me` (401 sin JWT + 200 con JWT + campos sensibles ausentes).
  - `/auth/resend-verification` (mensaje generico + invalida token previo).
  - `/auth/confirm-change-password/{token}` (token valido, invalido, reutilizado).
  - Redireccion web en `GET /auth/verify-email/{token}` (header Accept: text/html → 3xx).
  - Proteccion de `/metrics` con `Metrics:ApiKey` (sin key → 401, con key correcta → 200).

- [ ] Documentar y automatizar precondicion Docker para tests de integracion:
  - En local ahora fallan todos si Docker no esta disponible.
  - Definir check temprano/mensaje amigable o estrategia de skip explicita fuera de CI.

## P2 - Media prioridad

- [ ] Definir estrategia formal de migraciones de esquema:
  - Hoy depende de `script_DB.sql` manual.
  - Evitar drift entre codigo, tests y base real.

- [ ] Limpiar modelos legacy no usados o desalineados con schema actual:
  - Ejemplos: propiedades heredadas en `Usuario` y modelos residuales.

- [ ] Mejorar hardening HTTP:
  - Evaluar `Content-Security-Policy` y `Strict-Transport-Security` en produccion.

- [ ] Revisar indices SQL para redundancias y faltantes:
  - Confirmar necesidad de indices duplicados y cobertura de consultas frecuentes.

## P3 - Mejora continua

- [ ] Mantener tabla de trazabilidad "cambio de contrato -> test -> documentacion":
  - Cada cambio de endpoint/DTO debe actualizar tests y docs en el mismo PR.

- [ ] Estandarizar checklist de PR:
  - Build local, unit tests, integration tests (si aplica), docs, y seguridad de configuracion.
