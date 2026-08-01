using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public interface IControlAsignacionRepository
    {
        // Idempotente: si ya existe una asignación para ese (ControlPersonaId, EventId), no
        // falla ni la modifica; sólo crea el documento la primera vez.
        Task AsignarAsync(string controlPersonaId, string eventId, string assignedByPersonaId);

        Task<bool> ExisteAsignacionAsync(string controlPersonaId, string eventId);
    }
}
