using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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

            // Capturar el cuerpo de la solicitud (si es necesario)
            var requestBody = string.Empty;
            if (ShouldLogRequestBody(context))
            {
                context.Request.EnableBuffering();
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, true, 4096, true);
                requestBody = await reader.ReadToEndAsync();
                context.Request.Body.Position = 0; // Rebobinar para que otros puedan leerlo
            }

            // Registrar la solicitud entrante
            _logger.LogInformation(
                "Solicitud entrante: {RequestMethod} {RequestPath} | Usuario: {UserId} | RequestId: {RequestId}",
                context.Request.Method,
                context.Request.Path,
                userId,
                requestId);

            // Capturar el cuerpo de la respuesta
            var originalBodyStream = context.Response.Body;
            using var responseBodyStream = new MemoryStream();
            context.Response.Body = responseBodyStream;

            try
            {
                // Continuar con el pipeline
                await _next(context);

                // Detener el cronómetro
                stopwatch.Stop();

                // Capturar el cuerpo de la respuesta (si es necesario)
                var responseBody = string.Empty;
                if (ShouldLogResponseBody(context))
                {
                    responseBodyStream.Seek(0, SeekOrigin.Begin);
                    using var reader = new StreamReader(responseBodyStream);
                    responseBody = await reader.ReadToEndAsync();
                    responseBodyStream.Seek(0, SeekOrigin.Begin);
                }

                // Registrar la respuesta
                _logger.LogInformation(
                    "Respuesta completada: {StatusCode} en {ElapsedMilliseconds}ms | {RequestMethod} {RequestPath} | Usuario: {UserId} | RequestId: {RequestId}",
                    context.Response.StatusCode,
                    stopwatch.ElapsedMilliseconds,
                    context.Request.Method,
                    context.Request.Path,
                    userId,
                    requestId);

                // Copiar la respuesta al stream original
                await responseBodyStream.CopyToAsync(originalBodyStream);
            }
            catch (Exception ex)
            {
                // Detener el cronómetro en caso de error
                stopwatch.Stop();

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

                // Restaurar el cuerpo original
                context.Response.Body = originalBodyStream;

                // Devolver una respuesta de error apropiada
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync("Se produjo un error interno del servidor.");

                return;
            }
            finally
            {
                // Asegurarse de restaurar el stream original
                context.Response.Body = originalBodyStream;
            }
        }

        private bool ShouldLogRequestBody(HttpContext context)
        {
            // Solo registrar cuerpos de solicitud para POST, PUT, PATCH
            return context.Request.Method == "POST" ||
                   context.Request.Method == "PUT" ||
                   context.Request.Method == "PATCH";
        }

        private bool ShouldLogResponseBody(HttpContext context)
        {
            // Evitar registrar respuestas que contengan datos sensibles o archivos grandes
            return context.Response.StatusCode >= 400 && // Solo errores
                   context.Response.ContentType?.StartsWith("application/json") == true;
        }
    }
}