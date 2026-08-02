using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    // Altas privilegiadas (Admin/Organizador/Control) sobre el modelo nuevo Persona+Usuario+
    // UsuarioRol+IdentidadExterna (docs/security-refactor-plan.md §2.2, Etapa 3). El alta de
    // Cliente vive en AuthService (POST /api/auth/sync, §2.1), no acá.
    public class UserService : IUserService
    {
        // Ver docs/security-refactor-plan.md §2.1 punto 4 y §2.3: usado por AuthService para el
        // AssignedBy de las altas de Cliente autoprovisionadas vía /api/auth/sync.
        public const string SelfRegistrationActor = "SELF_REGISTRATION";

        private const string RolAdministrador = "ADMINISTRADOR";
        private const string RolOrganizador = "ORGANIZADOR";
        private const string RolControl = "CONTROL";

        private readonly IUsuarioRepository _usuarioRepository;
        private readonly IIdentidadHuerfanaRepository _identidadHuerfanaRepository;
        private readonly IEventService _eventService;
        private readonly IIdentityProvider _identityProvider;
        private readonly IAuthenticatedPersonaResolver _personaResolver;
        private readonly IControlAsignacionRepository _controlAsignacionRepository;
        private readonly ILogger<UserService> _logger;

        public UserService(
            IUsuarioRepository usuarioRepository,
            IIdentidadHuerfanaRepository identidadHuerfanaRepository,
            IEventService eventService,
            IIdentityProvider identityProvider,
            IAuthenticatedPersonaResolver personaResolver,
            IControlAsignacionRepository controlAsignacionRepository,
            ILogger<UserService> logger)
        {
            _usuarioRepository = usuarioRepository;
            _identidadHuerfanaRepository = identidadHuerfanaRepository;
            _eventService = eventService;
            _identityProvider = identityProvider;
            _personaResolver = personaResolver;
            _controlAsignacionRepository = controlAsignacionRepository;
            _logger = logger;
        }

        public Task<UsuarioProvisioningResult> RegisterAdminAsync(string assignedBy, string email, string password)
        {
            return ProvisionarConCompensacionAsync(email, password, null, RolAdministrador, assignedBy);
        }

        public Task<UsuarioProvisioningResult> RegisterOrganizadorAsync(string assignedBy, string email, string password)
        {
            return ProvisionarConCompensacionAsync(email, password, null, RolOrganizador, assignedBy);
        }

        public async Task<UsuarioProvisioningResult> RegisterControlAsync(string assignedBy, string userName, string password, string eventId)
        {
            // El organizador autenticado se resuelve a su PersonaId, y el evento se lee de
            // Firestore y se compara contra esa PersonaId (nunca contra un valor enviado por el
            // cliente). Si el evento no existe o pertenece a otro organizador, no se crea nada
            // en el proveedor de identidad ni en Firestore.
            var organizadorPersonaId = await _personaResolver.ResolvePersonaIdAsync(assignedBy);

            var evento = await _eventService.GetEventEntityByIdAsync(eventId);
            if (evento == null) throw new EventNotFoundException(eventId);
            if (evento.OrganizadorPersonaId != organizadorPersonaId) throw new EventOwnershipException(eventId, assignedBy);

            var email = $"{userName}@control.hoydonde.com";
            var result = await ProvisionarConCompensacionAsync(email, password, userName, RolControl, assignedBy);

            await _controlAsignacionRepository.AsignarAsync(result.PersonaId, eventId, organizadorPersonaId);

            return result;
        }

        // API-MVP 3 (docs/api-mvp-plan.md §4). No crea ninguna identidad ni cuenta nueva: solo
        // valida ownership del evento destino, elegibilidad del Control y ámbito del
        // organizador, y reutiliza IControlAsignacionRepository.AsignarAsync (ya idempotente y
        // transaccional a nivel repositorio).
        public async Task<ControlAsignacion> AsignarControlExistenteAsync(string actorUid, string eventId, string controlPersonaId)
        {
            var organizadorPersonaId = await _personaResolver.ResolvePersonaIdAsync(actorUid);

            var evento = await _eventService.GetEventEntityByIdAsync(eventId);
            if (evento == null) throw new EventNotFoundException(eventId);
            if (evento.OrganizadorPersonaId != organizadorPersonaId) throw new EventOwnershipException(eventId, actorUid);

            // No se puede asignar a un evento Cancelado ni a uno Publicado ya Finalizado
            // (estado efectivo). Borrador, Publicado en curso y Publicado no iniciado sí admiten
            // asignación.
            if (evento.Estado == Event.EventStatus.Cancelado ||
                evento.GetEstadoEfectivo(DateTime.UtcNow) == Event.EventEffectiveStatus.Finalizado)
            {
                throw new EventoNoDisponibleParaAsignacionControlException(eventId);
            }

            var usuarioControl = await _usuarioRepository.GetByPersonaIdAsync(controlPersonaId);
            if (usuarioControl == null || !usuarioControl.IsActive)
                throw new ControlInvalidoException(controlPersonaId);

            var rolesActivos = await _usuarioRepository.GetRolCodigosActivosAsync(usuarioControl.Id);
            if (!rolesActivos.Contains(RolControl))
                throw new ControlInvalidoException(controlPersonaId);

            // Ámbito del organizador: el Control debe tener ya al menos una asignación (a
            // cualquier evento) creada por este mismo organizador. Evita que un organizador se
            // apropie de un Control administrado exclusivamente por otro.
            var perteneceAlOrganizador = await _controlAsignacionRepository.ExisteAsignacionPorAsignadorAsync(controlPersonaId, organizadorPersonaId);
            if (!perteneceAlOrganizador)
                throw new ControlAjenoException(controlPersonaId);

            await _controlAsignacionRepository.AsignarAsync(controlPersonaId, eventId, organizadorPersonaId);

            // Lectura posterior: si la asignación ya existía (no-op idempotente), esto devuelve
            // AssignedByPersonaId/CreatedAt de la primera asignación, no del intento actual.
            var asignacion = await _controlAsignacionRepository.GetAsignacionAsync(controlPersonaId, eventId);
            return asignacion!;
        }

        // Crea la identidad externa y, si eso tiene éxito, provisiona Persona+Usuario+
        // UsuarioRol+IdentidadExterna en una sola transacción (IUsuarioRepository.ProvisionarAsync,
        // Etapa 2). Si CreateIdentityAsync lanza IdentityEmailAlreadyExistsException, se propaga
        // tal cual: no se creó nada en esta llamada, así que no hay nada que compensar ni ninguna
        // cuenta existente que tocar.
        private async Task<UsuarioProvisioningResult> ProvisionarConCompensacionAsync(
            string email, string password, string? displayName,
            string rolCodigo, string assignedBy)
        {
            var identity = await _identityProvider.CreateIdentityAsync(email, password, displayName);

            var personaId = Guid.NewGuid().ToString();
            var usuarioId = Guid.NewGuid().ToString();

            try
            {
                var request = new UsuarioProvisioningRequest(
                    personaId, usuarioId, identity.IdentityProvider, identity.ExternalSubjectId,
                    email, rolCodigo, assignedBy, FullName: displayName);

                return await _usuarioRepository.ProvisionarAsync(request);
            }
            catch (Exception original)
            {
                await CompensarAsync(identity.ExternalSubjectId, identity.IdentityProvider, email, rolCodigo, original);
                throw;
            }
        }

        // La identidad borrada acá es siempre la que creó esta misma llamada (nunca una
        // preexistente: ese caso ya terminó en IdentityEmailAlreadyExistsException más arriba,
        // antes de que exista algo que compensar).
        private async Task CompensarAsync(string externalSubjectId, string identityProvider, string email, string rolCodigo, Exception original)
        {
            try
            {
                await _identityProvider.DeleteIdentityAsync(externalSubjectId);
            }
            catch (Exception compensationError)
            {
                try
                {
                    await _identidadHuerfanaRepository.RegistrarAsync(new IdentidadHuerfana
                    {
                        IdentityProvider = identityProvider,
                        ExternalSubjectId = externalSubjectId,
                        Email = email,
                        RolCodigoSolicitado = rolCodigo,
                        ErrorOriginal = original.ToString(),
                        ErrorCompensacion = compensationError.ToString(),
                    });
                }
                catch (Exception registrationError)
                {
                    // Ni siquiera se pudo dejar constancia en identidades_huerfanas: logging
                    // estructurado con los tres errores para no perder rastro por completo.
                    _logger.LogError(original,
                        "Aprovisionamiento fallido sin compensar: identidad {Provider}#{ExternalSubjectId} (rol {RolCodigo}) quedó huérfana y no se pudo borrar ni registrar.",
                        identityProvider, externalSubjectId, rolCodigo);
                    _logger.LogError(compensationError,
                        "Fallo al compensar (DeleteIdentityAsync) la identidad huérfana {Provider}#{ExternalSubjectId}.",
                        identityProvider, externalSubjectId);
                    _logger.LogError(registrationError,
                        "Fallo también al registrar IdentidadHuerfana para {Provider}#{ExternalSubjectId}.",
                        identityProvider, externalSubjectId);
                }
            }
            // El error original nunca se oculta: siempre se relanza en el catch de
            // ProvisionarConCompensacionAsync, sin importar el resultado de la compensación.
        }
    }
}
