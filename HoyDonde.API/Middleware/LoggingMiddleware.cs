using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HoyDonde.API.Middleware
{
    public class LoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LoggingMiddleware> _logger;

        public LoggingMiddleware(RequestDelegate next, ILogger<LoggingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Generar un ID único para cada solicitud
            var requestId = Guid.NewGuid().ToString();
            context.Items["RequestId"] = requestId;

            // Obtener información del usuario (si está autenticado)
            var userId = context.User?.Identity?.IsAuthenticated == true
                ? context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value ?? "anónimo"
                : "anónimo";

            // Crear un cronómetro para medir el tiempo de respuesta
            var stopwatch = Stopwatch.StartNew();

            // Nunca se bufferiza ni se lee el cuerpo de la solicitud acá: nada en este
            // middleware lo loguea, y hacerlo sin usarlo arriesgaría exponer passwords/tokens
            // en logs (p. ej. RegisterAdminDto/RegisterOrganizadorDto/RegisterControlDto
            // viajan con Password en el body de POST /api/users/*).

            // Registrar la solicitud entrante
            _logger.LogInformation(
                "Solicitud entrante: {RequestMethod} {RequestPath} | Usuario: {UserId} | RequestId: {RequestId}",
                context.Request.Method,
                context.Request.Path,
                userId,
                requestId);

            try
            {
                // Continuar con el pipeline
                await _next(context);

                // Detener el cronómetro
                stopwatch.Stop();

                // Registrar la respuesta
                _logger.LogInformation(
                    "Respuesta completada: {StatusCode} en {ElapsedMilliseconds}ms | {RequestMethod} {RequestPath} | Usuario: {UserId} | RequestId: {RequestId}",
                    context.Response.StatusCode,
                    stopwatch.ElapsedMilliseconds,
                    context.Request.Method,
                    context.Request.Path,
                    userId,
                    requestId);
            }
            catch (Exception ex)
            {
                // Detener el cronómetro en caso de error
                if (stopwatch.IsRunning) stopwatch.Stop();

                // Registrar la excepción
                _logger.LogError(
                    ex,
                    "Error no controlado: {ErrorMessage} | {RequestMethod} {RequestPath} | Usuario: {UserId} | Tiempo: {ElapsedMilliseconds}ms | RequestId: {RequestId}",
                    ex.Message,
                    context.Request.Method,
                    context.Request.Path,
                    userId,
                    stopwatch.ElapsedMilliseconds,
                    requestId);

                throw; // Rethrow to let ExceptionMiddleware or the framework handle it
            }
        }
    }
}
