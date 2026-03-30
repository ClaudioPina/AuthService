using Microsoft.OpenApi.Models;

namespace AuthService.Api.Configuration
{
    /// <summary>
    /// Métodos de extensión para configurar Swagger/OpenAPI.
    /// Se extrae de Program.cs para mantenerlo limpio.
    /// Un "método de extensión" en C# es un método estático que se puede llamar
    /// como si fuera parte de la clase que recibe (en este caso IServiceCollection).
    /// </summary>
    public static class SwaggerConfig
    {
        /// <summary>
        /// Registra Swagger con soporte para autenticación JWT Bearer.
        /// Llama a este método desde Program.cs con: builder.Services.AddSwaggerWithJwt()
        /// </summary>
        public static IServiceCollection AddSwaggerWithJwt(this IServiceCollection services)
        {
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo
                {
                    Title       = "AuthService API",
                    Version     = "v1",
                    Description = "Microservicio de autenticación: registro, login, sesiones y recuperación de contraseña."
                });

                // Agrega el botón "Authorize" en Swagger UI para pegar el JWT.
                // Nota: el requerimiento de Bearer se aplica por endpoint via RequireAuthorization(),
                // no globalmente, para que los endpoints públicos aparezcan correctamente en la documentación.
                c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Name         = "Authorization",
                    Type         = SecuritySchemeType.Http,
                    Scheme       = "bearer",
                    BearerFormat = "JWT",
                    In           = ParameterLocation.Header,
                    Description  = "Ingresa tu JWT aquí. Ejemplo: Bearer {token}"
                });
            });

            return services;
        }

        /// <summary>
        /// Activa la interfaz de Swagger UI.
        /// Solo se llama en ambiente Development — en producción queda deshabilitado.
        /// </summary>
        public static WebApplication UseSwaggerInDevelopment(this WebApplication app)
        {
            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI(c =>
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "AuthService v1"));
            }
            return app;
        }
    }
}
