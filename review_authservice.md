# Revisión del Proyecto: AuthService

He completado una revisión exhaustiva del código fuente, el esquema de la base de datos y la arquitectura del microservicio **AuthService**. A continuación, presento un análisis de los puntos positivos y las áreas clave de mejora.

## 🌟 Puntos Positivos y Aciertos Arquitectónicos

1. **Uso de Minimal APIs (.NET 8):** El proyecto tiene un [Program.cs](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Program.cs) bien organizado y aprovecha correctamente las Minimal APIs, lo que reduce la carga de configuración y mejora el rendimiento.
2. **Modelo Híbrido Stateful/Stateless:** La decisión de usar JWT junto con sesiones almacenadas en la base de datos (y la tabla `SESIONES_USUARIOS`) es excelente. Permite revocar accesos en tiempo real, lo que soluciona el problema tradicional de JWT de no poder invalidar tokens comprometidos.
3. **Seguridad en Contraseñas:** 
   - El uso de **BCrypt** para el cifrado (algoritmo fuerte y adaptable) es el estándar de la industria.
   - Las validaciones robustas en [PasswordPolicy.cs](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Utils/PasswordPolicy.cs) (mínimo 8 caracteres, mayúsculas, minúsculas, números y símbolos) garantizan contraseñas fuertes.
4. **Validación Segura de Sesiones:** El `ValidarSesionMiddleware` intercepta efectivamente los endpoints protegidos y previene el acceso si la sesión en BD ha sido invalidada.
5. **Separación de Responsabilidades:** El uso del patrón [Repository](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Repositories/UsuariosRepository.cs#12-16) desacopla adecuadamente la lógica de acceso a datos de los controladores/rutas, manteniendo el código limpio.

---

## ⚠️ Áreas de Mejora y Hallazgos Críticos

A continuación se detallan los puntos que requieren atención para garantizar la robustez, seguridad y coherencia del sistema:

### 1. Inconsistencia con la "Lógica de Negocio" en Base de Datos
El [README.md](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/README.md) declara explícitamente: *"No contiene lógica de negocio (gastos, productos, órdenes, etc.). Su única responsabilidad es identidad y seguridad"*. Sin embargo, el archivo [script_DB.sql](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/script_DB.sql) incluye scripts de inserción (líneas 246 en adelante) para tablas que no están documentadas ni relacionadas con Auth, tales como:
- `TIPO_TRANSACCIONES`
- `ORGANIZACION` y `USUARIO_ORGANIZACION`
- `CATEGORIAS`
- `TRANSACCIONES` y `ARCHIVOS_TRANSACCIONES`
Además, no hay instrucciones `CREATE TABLE` para estas entidades, por lo que **el script SQL fallaría al ejecutarse** en una base de datos limpia de AuthService. **Solución:** Eliminar completamente cualquier referencia a estas tablas en el script del microservicio.

### 2. Residuos de Base de Datos (Columnas de Auditoría Multi-tenant)
En las tablas de identidad pura como `USUARIOS`, `SESIONES_USUARIOS`, `VERIFICACION_EMAIL` y `RESET_PASSWORD`, existen columnas de auditoría llamadas `propietario` (NOT NULL) y `usuario`.
- Requerir `propietario` en la creación de usuarios ([CrearUsuarioLocalAsync](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Repositories/UsuariosRepository.cs#38-70) inyecta un **`1`** hardcodeado) revela que estas tablas fueron adaptadas de un sistema anterior acoplado a un dominio de negocio específico.
- **Solución:** En un microservicio de auth independiente, el propio usuario es su "dueño", por lo que estas columnas (`propietario`) simplemente agregan ruido y complejidad innecesaria. Se recomienda eliminarlas de las tablas de Auth.

### 3. Diferencia de Case Sensitivity en Docker (Riesgo de build)
- En el [README.md](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/README.md) se especifica crear la carpeta llamada `Wallet_authservice` (con 'a' minúscula).
- En el [.gitignore](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/.gitignore) se ignora `Wallet_AuthService/`.
- En el [Dockerfile](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/Dockerfile) se hace `COPY AuthService.Api/Wallet_AuthService`.
- **Riesgo:** Windows no distingue mayúsculas, por lo que localmente compila. Al momento de generar la imagen en Linux/Docker, dará un error `COPY failed` porque buscará exactamente `Wallet_AuthService` pero podría encontrar la carpeta con `a` minúscula.
- **Solución:** Unificar y utilizar exactamente el mismo casing en todo el proyecto (ej. `Wallet_AuthService`).

### 4. Valores Hardcodeados
En el código se definieron strings en duro que, de cara al uso del sistema en diferentes entornos (local, dev, stg, prod), deberían estar en `appsettings.json`:
- **URLs de los correos:** En [Program.cs](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Program.cs) existen referencias fijas como `var baseUrl = "https://localhost:5001";` para construir el link de `verify-email` y de `forgot-password`.
- **Tiempo de Expiración del Access Token:** En el endpoint de Auth se responde estáticamente `accessTokenExpiresInMinutes = 15;` sin importar la configuración en `JwtGenerator`. Podría causar inconsistencias para los clientes SPA.

### 5. Configuración de Oracle Wallet (Código vs README)
El [README.md](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/README.md) documenta que la configuración en [Program.cs](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Program.cs) se hace con `Path.Combine(env.ContentRootPath, "Wallet_authservice")`. Sin embargo, en [Program.cs](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Program.cs) este enfoque está ausente o comentado, y en su lugar el código asume ciegamente leer la variable de entorno:
```csharp
OracleConfiguration.TnsAdmin = Environment.GetEnvironmentVariable("TNS_ADMIN");
```
Aunque el Dockerfile la define correctamente, si corremos el proyecto localmente mediante `dotnet run` (como sugiere el README), fallaría la conexión si el OS del desarrollador no tiene la variable `TNS_ADMIN` previamente exportada.
**Solución:** Restaurar lógica de fallback o el uso del `Path.Combine` en la inicialización si la variable de entorno viene nula.

### 6. Duplicación de Dependencias de Oracle
En el archivo del proyecto [AuthService.Api.csproj](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/AuthService.Api.csproj) (según el README), se han agregado ambos paquetes:
- `Oracle.ManagedDataAccess`
- `Oracle.ManagedDataAccess.Core`
Para aplicaciones en **.NET Core/.NET 8**, solo es estrictamente necesario y recomendado usar `Oracle.ManagedDataAccess.Core`.

---

## 🎯 Conclusión
El microservicio tiene una **muy buena base de patrones modernos de seguridad**. Al solucionar la "contaminación" de la base de datos con tablas de negocio y estandarizar la forma en la que se leen las variables de entorno/rutas de la wallet, el proyecto estará listo para ser utilizado como una pieza agnóstica de infraestructura en tus futuras aplicaciones.
