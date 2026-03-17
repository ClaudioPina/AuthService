# Plan de Migración a PostgreSQL

El objetivo de esta migración es cambiar la base de datos subyacente de Oracle a PostgreSQL, lo que permitirá un despliegue mucho más ligero en un VPS y eliminará la necesidad operativa de la Wallet de Oracle.

## Cambios Propuestos

### 1. Dependencias del Proyecto
#### [MODIFY] [AuthService.Api/AuthService.Api.csproj](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/AuthService.Api.csproj)
- [DELETE] Paquetes `Oracle.ManagedDataAccess` y `Oracle.ManagedDataAccess.Core`.
- [NEW] Añadir paquete `Npgsql` (el proveedor de datos nativo y más utilizado para PostgreSQL en .NET).

### 2. Contexto de Base de Datos
#### [DELETE] [AuthService.Api/Data/OracleDbContext.cs](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Data/OracleDbContext.cs)
#### [NEW] `AuthService.Api/Data/AppDbContext.cs`
- Crear la nueva clase `AppDbContext` que utilizará `NpgsqlConnection`.
- Actualizar las políticas de reintentos (Polly) para capturar excepciones específicas de Postgres (`PostgresException`).

### 3. Repositorios (Capa de Acceso a Datos)
#### [MODIFY] Todos los repositorios
Archivos afectados:
- [AuthService.Api/Repositories/UsuariosRepository.cs](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Repositories/UsuariosRepository.cs)
- [AuthService.Api/Repositories/SesionesUsuariosRepository.cs](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Repositories/SesionesUsuariosRepository.cs)
- [AuthService.Api/Repositories/VerificacionEmailRepository.cs](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Repositories/VerificacionEmailRepository.cs)
- [AuthService.Api/Repositories/ResetPasswordRepository.cs](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Repositories/ResetPasswordRepository.cs)

**Cambios técnicos en SQL:**
- Cambiar prefijos de parámetros de Oracle (`:param`) al estándar usado nativamente por Npgsql (`@param`).
- Reemplazar las referencias a `OracleCommand`, `OracleConnection` y `OracleDbType` por sus contrapartes `NpgsqlCommand`, `NpgsqlConnection` y `NpgsqlDbType`.
- Ajustes de sintaxis SQL específicos de Postgres:
  - Cambiar `SYSDATE` a `CURRENT_TIMESTAMP`.
  - Simplificar las sentencias `RETURNING` (Postgres las maneja directamente con un `ExecuteScalar` sin necesidad de parámetros de salida complejos como en Oracle).

### 4. Configuración Principal
#### [MODIFY] [AuthService.Api/Program.cs](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Program.cs)
- Eliminar la inyección y configuración de [OracleDbContext](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/AuthService.Api/Data/OracleDbContext.cs#7-47).
- Registrar el nuevo `AppDbContext`.
- Eliminar todo el código relacionado con la lectura y asignación de `TNS_ADMIN` y variables de entorno para Oracle Wallet.

### 5. Script de Base de Datos
#### [MODIFY] [script_DB.sql](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/script_DB.sql)
- Cambiar tipos de datos de Oracle a Postgres (ej: `VARCHAR2` -> `VARCHAR`, `NUMBER` -> `INTEGER`, `BIGINT` o `SMALLINT`).
- Eliminar sentencias `CREATE SEQUENCE` y los *Triggers* (Postgres maneja esto elegantemente con columnas `SERIAL` o `GENERATED ALWAYS AS IDENTITY`).
- Adaptar funciones de fecha (`SYSDATE` -> `CURRENT_TIMESTAMP`).

### 6. Despliegue y Documentación
#### [MODIFY] [Dockerfile](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/Dockerfile)
- Eliminar las instrucciones `COPY` y las variables de entorno relacionadas con Oracle Wallet, reduciendo el tamaño de la imagen final.

#### [MODIFY] [README.md](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/README.md)
- Actualizar la sección de tecnologías y base de datos de Oracle a PostgreSQL.
- Actualizar las instrucciones de configuración del `appsettings.json` para mostrar un *Connection String* propio de Postgres.

## Plan de Verificación

### Verificación Estática y de Compilación
Ejecutar `dotnet build` para comprobar que:
- No existen errores de sintaxis tras reemplazar las clases de Oracle por Npgsql.
- Los tipos y parámetros estén correctamente asignados en todos los repositorios.

### Verificación Manual Sugerida
Una vez completada la migración, se te solicitará realizar la siguiente comprobación:
1. Levantar una instancia local de PostgreSQL (mediante Docker o instalación nativa).
2. Crear la base de datos y ejecutar el nuevo [script_DB.sql](file:///c:/Users/Claudio.Pi%C3%B1a/Documents/Proyectos%20Personales/AuthService-main/script_DB.sql).
3. Actualizar tu `appsettings.json` con las credenciales de conexión locales.
4. Ejecutar el proyecto (`dotnet run`) y realizar un flujo completo: registro, login, verificación de token en Swagger.
