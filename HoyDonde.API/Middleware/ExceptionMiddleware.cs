using HoyDonde.API.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HoyDonde.API.Middleware
{
    // Punto central único de mapeo excepción-tipada -> HTTP (docs/api-mvp-plan.md §5): ningún
    // controller mapea excepciones de dominio a códigos HTTP; todos las dejan propagar hasta acá.
    // Contrato público estable para cualquier error (ver ErrorResponse): Code/Message/TraceId,
    // con Errors reservado exclusivamente a errores de validación de campos (ModelState). El
    // Message público de cada excepción tipada es fijo por Code (ver ExceptionCatalog), nunca el
    // texto crudo de una excepción de infraestructura (Firestore/Firebase) ni datos internos
    // (UID, ExternalSubjectId, nombres de colección, stack traces): esos solo van al log
    // estructurado y, para un 500, al campo opcional Detail fuera de Production.
    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly IHostEnvironment _environment;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        // Catálogo estable Tipo-de-excepción -> (HTTP status, Code público, mensaje público).
        // El mensaje público es una función del mensaje de la excepción salvo que ese mensaje
        // pueda contener datos que nunca deben viajar al cliente (p. ej. EventOwnershipException
        // incluye el UID del actor en su Message, usado solo para logging interno).
        private static readonly IReadOnlyDictionary<Type, (HttpStatusCode Status, string Code, Func<Exception, string> PublicMessage)> ExceptionCatalog =
            new Dictionary<Type, (HttpStatusCode, string, Func<Exception, string>)>
            {
                [typeof(EventValidationException)] = (HttpStatusCode.BadRequest, "EVENT_VALIDATION_ERROR", ex => ex.Message),
                [typeof(ArgumentException)] = (HttpStatusCode.BadRequest, "VALIDATION_ERROR", ex => ex.Message),

                [typeof(EventNotFoundException)] = (HttpStatusCode.NotFound, "EVENT_NOT_FOUND", ex => ex.Message),
                [typeof(TicketTypeInvalidoException)] = (HttpStatusCode.NotFound, "TICKET_TYPE_INVALID", ex => ex.Message),
                [typeof(ControlInvalidoException)] = (HttpStatusCode.NotFound, "CONTROL_INVALID", ex => ex.Message),
                [typeof(RolNoEncontradoException)] = (HttpStatusCode.NotFound, "ROLE_NOT_FOUND", ex => ex.Message),
                [typeof(AccionNoEncontradaException)] = (HttpStatusCode.NotFound, "ACTION_NOT_FOUND", ex => ex.Message),
                [typeof(UsuarioNoEncontradoException)] = (HttpStatusCode.NotFound, "USER_NOT_FOUND", ex => ex.Message),

                // El Message de EventOwnershipException incluye el UID del actor (uso interno de
                // logging exclusivamente): la respuesta pública usa un texto fijo, nunca ex.Message.
                [typeof(EventOwnershipException)] = (HttpStatusCode.Forbidden, "EVENT_OWNERSHIP", _ => "No tenés permiso sobre este evento."),
                [typeof(ControlAjenoException)] = (HttpStatusCode.Forbidden, "CONTROL_FOREIGN", ex => ex.Message),

                [typeof(EventInvalidTransitionException)] = (HttpStatusCode.Conflict, "EVENT_INVALID_TRANSITION", ex => ex.Message),
                [typeof(EventMissingTicketTypesException)] = (HttpStatusCode.Conflict, "EVENT_MISSING_TICKET_TYPES", ex => ex.Message),
                [typeof(EventNotEditableException)] = (HttpStatusCode.Conflict, "EVENT_NOT_EDITABLE", ex => ex.Message),
                [typeof(EventoNoDisponibleParaCompraException)] = (HttpStatusCode.Conflict, "EVENT_NOT_AVAILABLE_FOR_PURCHASE", ex => ex.Message),
                [typeof(EventoNoDisponibleParaAsignacionControlException)] = (HttpStatusCode.Conflict, "EVENT_NOT_AVAILABLE_FOR_CONTROL_ASSIGNMENT", ex => ex.Message),
                [typeof(StockInsuficienteException)] = (HttpStatusCode.Conflict, "TICKET_STOCK_INSUFFICIENT", ex => ex.Message),
                [typeof(IdentityEmailAlreadyExistsException)] = (HttpStatusCode.Conflict, "IDENTITY_EMAIL_ALREADY_EXISTS", ex => ex.Message),
                [typeof(RolYaExisteException)] = (HttpStatusCode.Conflict, "ROLE_ALREADY_EXISTS", ex => ex.Message),
                [typeof(AccionYaExisteException)] = (HttpStatusCode.Conflict, "ACTION_ALREADY_EXISTS", ex => ex.Message),
                [typeof(UltimoAdministradorException)] = (HttpStatusCode.Conflict, "LAST_ADMINISTRATOR", ex => ex.Message),
            };

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
            catch (IdentityNotProvisionedException ex)
            {
                // Token válido, pero sin Usuario/Persona aprovisionado en el modelo nuevo
                // (docs/security-refactor-plan.md §1/§4): comportamiento explícito y único,
                // centralizado acá para no duplicarlo endpoint por endpoint. El UID sólo se
                // registra en el log interno; el mensaje público (ex.Message) ya es genérico y
                // no lo incluye.
                _logger.LogWarning(
                    "Identidad sin aprovisionar intentó una acción autenticada. UID: {ExternalSubjectId} | Ruta: {Path}",
                    ex.ExternalSubjectId,
                    httpContext.Request.Path);

                await WriteErrorAsync(httpContext, HttpStatusCode.Forbidden, "IDENTITY_NOT_PROVISIONED", ex.Message, errors: null);
            }
            catch (Exception ex) when (ExceptionCatalog.TryGetValue(ex.GetType(), out var mapped))
            {
                var requestId = GetRequestId(httpContext);
                _logger.LogWarning(
                    ex,
                    "Excepción de dominio {Code}: {Message} | Ruta: {Path} | RequestId: {RequestId}",
                    mapped.Code, ex.Message, httpContext.Request.Path, requestId);

                await WriteErrorAsync(httpContext, mapped.Status, mapped.Code, mapped.PublicMessage(ex), errors: null);
            }
            catch (Exception ex)
            {
                var requestId = GetRequestId(httpContext);
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

                var publicMessage = _environment.IsDevelopment()
                    ? ex.Message
                    : "Ocurrió un error inesperado. Contactá al soporte indicando el RequestId.";
                var detail = _environment.IsDevelopment() ? ex.ToString() : null;

                await WriteErrorAsync(httpContext, HttpStatusCode.InternalServerError, "UNEXPECTED_ERROR", publicMessage, errors: null, detail: detail);
            }
        }

        // Reutiliza el RequestId ya fijado por LoggingMiddleware (que corre por dentro en el
        // pipeline) si existe; si no, lo genera y lo fija acá para que quede estable durante el
        // resto del procesamiento de esta misma request.
        private static string GetRequestId(HttpContext context)
        {
            if (context.Items["RequestId"] is string existing) return existing;

            var generated = Guid.NewGuid().ToString();
            context.Items["RequestId"] = generated;
            return generated;
        }

        private static async Task WriteErrorAsync(
            HttpContext context,
            HttpStatusCode status,
            string code,
            string message,
            IDictionary<string, string[]>? errors,
            string? detail = null)
        {
            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)status;

            var response = new ErrorResponse
            {
                Code = code,
                Message = message,
                TraceId = GetRequestId(context),
                Errors = errors,
                Detail = detail,
            };

            var jsonResponse = JsonSerializer.Serialize(response, JsonOptions);
            await context.Response.WriteAsync(jsonResponse);
        }

        // Usado también por Program.cs (ApiBehaviorOptions.InvalidModelStateResponseFactory) para
        // que los errores automáticos de ModelState sigan exactamente el mismo contrato.
        public static async Task WriteValidationErrorAsync(HttpContext context, IDictionary<string, string[]> errors)
        {
            await WriteErrorAsync(context, HttpStatusCode.BadRequest, "VALIDATION_ERROR", "Uno o más campos no son válidos.", errors);
        }
    }

    // Contrato público estable de error (docs/api-mvp-plan.md §5): Code (estable, comprobable por
    // tests), Message (texto público, nunca un mensaje crudo de Firestore/Firebase ni datos
    // internos), TraceId (correlación con los logs del servidor), Errors (solo para validación de
    // campos) y Detail (solo fuera de Production, para diagnóstico de un 500 en desarrollo).
    public class ErrorResponse
    {
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string TraceId { get; set; } = string.Empty;
        public IDictionary<string, string[]>? Errors { get; set; }
        public string? Detail { get; set; }
    }
}
