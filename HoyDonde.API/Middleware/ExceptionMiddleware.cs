using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;

namespace HoyDonde.API.Middleware
{
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IHostEnvironment _environment;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger,
            IHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext httpContext)
        {
            try
            {
                await _next(httpContext);
            }
            catch (Exception ex)
            {
                var requestId = httpContext.Items["RequestId"]?.ToString() ?? Guid.NewGuid().ToString();
                var userId = httpContext.User?.Identity?.IsAuthenticated == true
                    ? httpContext.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value ?? "anónimo"
                    : "anónimo";

                _logger.LogError(
                    ex,
                    "Excepción no controlada: {Message} | Ruta: {Path} | Usuario: {UserId} | RequestId: {RequestId}",
                    ex.Message,
                    httpContext.Request.Path,
                    userId,
                    requestId);

                await HandleExceptionAsync(httpContext, ex, requestId);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception, string requestId)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

            var response = new ErrorResponse
            {
                RequestId = requestId,
                Message = _environment.IsDevelopment()
                    ? exception.Message
                    : "Se produjo un error interno en el servidor."
            };

            if (_environment.IsDevelopment())
            {
                response.DetailedError = exception.ToString();
            }

            var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            await context.Response.WriteAsync(jsonResponse);
        }
    }

    public class ErrorResponse
    {
        public string RequestId { get; set; }
        public string Message { get; set; }
        public string DetailedError { get; set; }
    }
}