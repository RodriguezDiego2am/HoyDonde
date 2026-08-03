using HoyDonde.API.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public interface IUsuarioRepository
    {
        // Crea, en una única transacción Firestore, Persona + Usuario + UsuarioRol inicial +
        // IdentidadExterna. PersonaId/UsuarioId ya vienen generados en el request (el llamador
        // los genera antes de invocar esto, no se generan dentro de la transacción). Idempotente:
        // si ya existe una IdentidadExterna para ese (IdentityProvider, ExternalSubjectId), no
        // vuelve a escribir nada y devuelve los IDs ya existentes en vez de los del request
        // (docs/security-refactor-plan.md §6).
        Task<UsuarioProvisioningResult> ProvisionarAsync(UsuarioProvisioningRequest request);

        Task<string?> GetUsuarioIdByExternalSubjectAsync(string identityProvider, string externalSubjectId);

        Task<Usuario?> GetByIdAsync(string usuarioId);

        // Resolución batch por UsuarioId (docs/api-mvp-plan.md §11.3: ActorEmail del reporte de
        // auditoría de seguridad), por referencia directa al documento -no una query WhereIn-,
        // mismo patrón que EventService.GetEntitiesByIdsAsync. Nunca una lectura por actor.
        Task<IReadOnlyList<Usuario>> GetByIdsAsync(IEnumerable<string> usuarioIds);

        // Resuelve el Usuario dueño de una PersonaId dada (p. ej. el controlPersonaId recibido
        // en POST /api/events/{eventId}/controls/{controlPersonaId}). null si ninguna Usuario
        // tiene esa PersonaId.
        Task<Usuario?> GetByPersonaIdAsync(string personaId);

        // Resolución batch de varios Usuario por PersonaId (API-MVP 5, docs/api-mvp-plan.md §6):
        // evita una lectura individual por Control al listar los Controles del ámbito de un
        // Organizador o los asignados a un evento. Firestore WhereIn admite hasta 30 valores por
        // consulta; el volumen esperado del MVP (decenas de asignaciones, no miles) queda
        // cubierto sin paginar.
        Task<IReadOnlyList<Usuario>> GetByPersonaIdsAsync(IEnumerable<string> personaIds);

        Task<IReadOnlyList<string>> GetRolCodigosActivosAsync(string usuarioId);

        // Usados por el bootstrap del primer Administrador (docs/security-refactor-plan.md §5)
        // para verificar "ya existe un Administrador efectivo" sin conocer de antemano ningún
        // UsuarioId: recorre la collection group "roles" en vez de partir de una identidad.
        Task<IReadOnlyList<string>> GetUsuarioIdsConRolActivoAsync(string rolCodigo);

        // Administración de seguridad (docs/security-refactor-plan.md §6, Etapa 5). Cada método
        // escribe la mutación y su SecurityAudit en una única transacción Firestore.

        // Idempotente: si ya existe una asignación activa, no-op; si existe pero inactiva, la
        // reactiva coherentemente. Falla con UsuarioNoEncontradoException si el usuario no existe.
        Task AsignarRolAsync(string usuarioId, string rolCodigo, string assignedBy, SecurityAudit auditEntry);

        // Idempotente: quitar una asignación ya ausente o inactiva no falla. Falla con
        // UsuarioNoEncontradoException si el usuario no existe. Si rolCodigo == "ADMINISTRADOR",
        // aplica el guard transaccional del último Administrador (falla con
        // UltimoAdministradorException sin escribir nada si dejaría cero Administradores
        // efectivos).
        Task QuitarRolAsync(string usuarioId, string rolCodigo, SecurityAudit auditEntry);

        // Falla con UsuarioNoEncontradoException si el usuario no existe. No-op idempotente si
        // ya está en el estado pedido (no evalúa el guard ni audita). Si activo == false y no era
        // ya así, aplica el guard transaccional del último Administrador.
        Task SetActivoAsync(string usuarioId, bool activo, SecurityAudit auditEntry);

        // Catálogo mínimo de Usuarios para GET /api/security/usuarios
        // (docs/security-refactor-plan.md §6, Etapa 5): UsuarioId, PersonaId, email, estado y
        // roles activos. Nunca expone ExternalSubjectId ni ningún otro dato del proveedor de
        // identidad.
        Task<IReadOnlyList<UsuarioResumen>> GetAllAsync();
    }

    public record UsuarioResumen(
        string UsuarioId,
        string PersonaId,
        string Email,
        bool Activo,
        IReadOnlyList<string> RolesActivos);

    public record UsuarioProvisioningRequest(
        string PersonaId,
        string UsuarioId,
        string IdentityProvider,
        string ExternalSubjectId,
        string Email,
        string RolCodigo,
        string AssignedBy,
        string? FullName = null,
        string? Dni = null,
        string? PhoneNumber = null);

    public record UsuarioProvisioningResult(string PersonaId, string UsuarioId);
}
