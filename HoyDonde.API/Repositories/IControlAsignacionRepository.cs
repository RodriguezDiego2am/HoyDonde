using HoyDonde.API.Models;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public interface IControlAsignacionRepository
    {
        // Idempotente: si ya existe una asignación para ese (ControlPersonaId, EventId), no
        // falla ni la modifica; sólo crea el documento la primera vez.
        Task AsignarAsync(string controlPersonaId, string eventId, string assignedByPersonaId);

        Task<bool> ExisteAsignacionAsync(string controlPersonaId, string eventId);

        // Ámbito del organizador (docs/api-mvp-plan.md §4): true si ese Control ya tiene al
        // menos una asignación (a cualquier evento) creada por ese organizador. Un organizador
        // no puede apropiarse de un Control cuya única asignación previa la creó otro
        // organizador. Limit(1): no carga la colección completa en memoria.
        Task<bool> ExisteAsignacionPorAsignadorAsync(string controlPersonaId, string assignedByPersonaId);

        // Lectura puntual por el id determinístico "{ControlPersonaId}_{EventId}", usada para
        // devolver AssignedByPersonaId/CreatedAt tras un AsignarAsync (nuevo o ya existente),
        // preservando la metadata original en el caso idempotente.
        Task<ControlAsignacion?> GetAsignacionAsync(string controlPersonaId, string eventId);
    }
}
