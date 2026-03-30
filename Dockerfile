# ===============================
# Etapa 1: Build
# ===============================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar csproj y restaurar dependencias
COPY AuthService.Api/AuthService.Api.csproj AuthService.Api/
RUN dotnet restore AuthService.Api/AuthService.Api.csproj

# Copiar el resto del código
COPY . .
WORKDIR /src/AuthService.Api
RUN dotnet publish -c Release -o /app/publish

# ===============================
# Etapa 2: Runtime
# ===============================
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Crear un usuario sin privilegios para ejecutar la app.
# Buena práctica de seguridad: si hubiera una vulnerabilidad,
# el proceso no tendría acceso de root al contenedor.
RUN adduser --disabled-password --gecos "" appuser

COPY --from=build /app/publish .

# Cambiar propietario de los archivos al nuevo usuario
RUN chown -R appuser:appuser /app
USER appuser

# Exponer el puerto
EXPOSE 8080

# Ejecutar la app
ENTRYPOINT ["dotnet", "AuthService.Api.dll"]
