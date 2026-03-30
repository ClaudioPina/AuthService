# TODO — AuthService

Tareas pendientes ordenadas por prioridad. El código está completo y commiteado.

---

## 1. Verificación local (antes de todo lo demás)

- [ ] Crear `AuthService.Api/appsettings.json` basándote en `appsettings.example.json`
  - Genera una clave JWT segura: `openssl rand -base64 32`
  - Completa los datos de conexión a tu PostgreSQL local
  - Por ahora deja `Email.ResendApiKey` vacío (Development no envía emails reales)

- [ ] Verificar que el proyecto compila y corre localmente:
  ```bash
  dotnet build
  dotnet run --project AuthService.Api
  # Abrir http://localhost:5091/swagger
  ```

- [ ] Ejecutar los tests unitarios (no requieren Docker):
  ```bash
  dotnet test --filter "FullyQualifiedName~Unit"
  ```

- [ ] Ejecutar los tests de integración (requieren Docker corriendo):
  ```bash
  dotnet test --filter "FullyQualifiedName~Integration"
  ```

---

## 2. Configurar Resend (servicio de email)

- [ ] Crear cuenta gratuita en [resend.com](https://resend.com)
- [ ] Obtener la API key desde el dashboard de Resend
- [ ] Agregar la API key en `appsettings.json`:
  ```json
  "Email": { "ResendApiKey": "re_xxxx..." }
  ```
- [ ] Probar el flujo completo de registro: registrar un usuario y verificar que llega el email de verificación
- [ ] (Opcional, cuando tengas dominio propio) Configurar el dominio en Resend para usar tu propio remitente

---

## 3. Setup de Fly.io (primer deploy)

Estos pasos son únicos — solo se hacen una vez.

- [ ] Instalar Fly.io CLI:
  ```bash
  # Windows (PowerShell)
  iwr https://fly.io/install.ps1 -useb | iex
  ```

- [ ] Autenticarse:
  ```bash
  fly auth login
  ```

- [ ] Crear la base de datos PostgreSQL en Fly.io:
  ```bash
  fly postgres create --name authservice-db --region gru
  # Guarda las credenciales que te muestra — solo se muestran una vez
  ```

- [ ] Conectar la BD a la app (inyecta `DATABASE_URL` automáticamente):
  ```bash
  fly postgres attach authservice-db --app authservice
  ```

- [ ] Aplicar el esquema de base de datos en la BD de producción:
  ```bash
  fly postgres connect -a authservice-db
  # Una vez conectado, ejecutar el contenido de script_DB.sql
  ```

- [ ] Configurar los secrets de la app:
  ```bash
  fly secrets set \
    "Jwt__Key=<clave de mínimo 32 caracteres>" \
    "Email__ResendApiKey=re_xxxx..." \
    "App__BaseUrl=https://authservice.fly.dev" \
    "Cors__AllowedOrigins__0=https://tu-frontend.com"
  ```

- [ ] Primer deploy manual:
  ```bash
  fly deploy
  ```

- [ ] Verificar que la app responde:
  ```bash
  curl https://authservice.fly.dev/health
  # Esperado: {"status":"Healthy"}
  ```

---

## 4. Configurar GitHub Actions (CI/CD automático)

- [ ] Generar token de deploy para Fly.io:
  ```bash
  fly tokens create deploy -x 999999h
  # Copia el token generado
  ```

- [ ] Agregar el token como secret en GitHub:
  - Ir a: GitHub repo → Settings → Secrets and variables → Actions
  - Crear nuevo secret con nombre: `FLY_API_TOKEN`
  - Pegar el token copiado como valor

- [ ] Hacer push a `main` y verificar que los workflows se ejecutan correctamente:
  - `CI` debe pasar (build + todos los tests)
  - `Deploy a Fly.io` debe ejecutarse y completar el deploy

---

## 5. Tareas opcionales / mejoras futuras

- [ ] Configurar dominio personalizado en Fly.io (`fly certs add tu-dominio.com`)
- [ ] Agregar health check que verifique también la conexión a la BD:
  ```csharp
  builder.Services.AddHealthChecks()
      .AddNpgSql(connectionString); // NuGet: AspNetCore.HealthChecks.NpgSql
  ```
- [ ] Configurar alertas de error en Fly.io o integrar con un servicio externo (Sentry, etc.)
- [ ] Integrar OAuth (Google login) cuando el frontend lo requiera
- [ ] Agregar roles y permisos cuando la aplicación que consume AuthService los necesite
