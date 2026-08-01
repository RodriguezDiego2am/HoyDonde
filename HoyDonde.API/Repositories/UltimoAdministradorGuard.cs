using Google.Cloud.Firestore;
using HoyDonde.API.Models;
using System.Linq;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    // Guard transaccional de "Administrador efectivo" (docs/security-refactor-plan.md §6,
    // Etapa 5): Administrador efectivo = Usuario.IsActive + UsuarioRol(ADMINISTRADOR).Activo +
    // Rol(ADMINISTRADOR).Activo. Se invoca DENTRO de una transacción Firestore ya abierta (por
    // FirestoreRolRepository.SetActivoAsync o FirestoreUsuarioRepository.SetActivoAsync/
    // QuitarRolAsync), antes de cualquier escritura: todas las lecturas (Rol, collection group
    // query sobre usuarios/*/roles, y los Usuario correspondientes) ocurren acá, dentro de la
    // misma transacción, para que Firestore pueda detectar conflictos de concurrencia reales
    // (dos operaciones que leen/escriben los mismos documentos) y reintentar o abortar según
    // corresponda -sin depender de contadores externos ni de un chequeo previo fuera de la
    // transacción-.
    //
    // Los parámetros "forzado*" permiten contar el efecto HIPOTÉTICO de la mutación que se está
    // por aplicar (todavía no escrita) sin necesidad de una segunda pasada de lectura.
    internal static class UltimoAdministradorGuard
    {
        public const string RolAdministrador = "ADMINISTRADOR";

        private const string UsuariosCollection = "usuarios";
        private const string RolesSubcollectionName = "roles";
        private const string RolesCollection = "roles";

        public static async Task<int> ContarEfectivosAsync(
            Transaction transaction,
            FirestoreDb firestore,
            string? usuarioIdConEstadoForzado = null,
            bool usuarioForzadoActivo = false,
            string? usuarioIdConAsignacionForzada = null,
            bool asignacionForzadaActiva = false,
            bool? rolAdministradorForzadoActivo = null)
        {
            var rolRef = firestore.Collection(RolesCollection).Document(RolAdministrador);
            var rolSnapshot = await transaction.GetSnapshotAsync(rolRef);
            var rolActivo = rolAdministradorForzadoActivo
                ?? (rolSnapshot.Exists && rolSnapshot.ConvertTo<Rol>().Activo);

            if (!rolActivo) return 0;

            var query = firestore.CollectionGroup(RolesSubcollectionName).WhereEqualTo(nameof(UsuarioRol.Activo), true);
            var querySnapshot = await transaction.GetSnapshotAsync(query);

            // Misma precaución que FirestoreUsuarioRepository.GetUsuarioIdsConRolActivoAsync: la
            // colección raíz "roles" (catálogo) comparte Id de colección con la subcolección
            // usuarios/{Id}/roles, así que se descartan los documentos sin padre real bajo
            // "usuarios".
            var usuarioRefs = querySnapshot.Documents
                .Where(d => d.Id == RolAdministrador)
                .Select(d => d.Reference.Parent.Parent)
                .Where(usuarioRef => usuarioRef != null && usuarioRef.Parent.Id == UsuariosCollection)
                .Select(usuarioRef => usuarioRef!)
                .ToList();

            if (usuarioRefs.Count == 0) return 0;

            var usuarioSnapshots = await transaction.GetAllSnapshotsAsync(usuarioRefs);

            var efectivos = 0;
            foreach (var usuarioSnapshot in usuarioSnapshots)
            {
                if (!usuarioSnapshot.Exists) continue;
                var usuario = usuarioSnapshot.ConvertTo<Usuario>();

                var asignacionActiva = usuario.Id == usuarioIdConAsignacionForzada
                    ? asignacionForzadaActiva
                    : true; // ya viene filtrado Activo == true por la query

                var usuarioActivo = usuario.Id == usuarioIdConEstadoForzado
                    ? usuarioForzadoActivo
                    : usuario.IsActive;

                if (asignacionActiva && usuarioActivo) efectivos++;
            }

            return efectivos;
        }
    }
}
