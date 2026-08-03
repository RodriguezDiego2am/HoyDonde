using Google.Cloud.Firestore;
using Grpc.Core;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HoyDonde.API.Repositories
{
    public class FirestoreRolRepository : IRolRepository
    {
        private readonly FirestoreDb _firestore;
        private const string CollectionName = "roles";
        private const string AccionesSubcollectionName = "acciones";
        private const string AccionesCollectionName = "acciones";
        private const string UsuariosCollectionName = "usuarios";
        // OJO: la subcolección usuarios/{UsuarioId}/roles (UsuarioRol, Etapa 2) comparte el mismo
        // nombre de colección ("roles") que CollectionName de acá arriba (catálogo de Rol,
        // Etapa 1). Una CollectionGroup("roles") devuelve documentos de ambos orígenes -ver
        // EliminarAsync más abajo, mismo criterio de descarte que
        // FirestoreUsuarioRepository.GetUsuarioIdsConRolActivoAsync/UltimoAdministradorGuard-.
        private const string UsuarioRolesSubcollectionName = "roles";

        public FirestoreRolRepository(FirestoreDb firestore)
        {
            _firestore = firestore;
        }

        public async Task<bool> ExistsAsync(string codigo)
        {
            var snapshot = await _firestore.Collection(CollectionName).Document(codigo).GetSnapshotAsync();
            return snapshot.Exists;
        }

        public async Task CreateAsync(Rol rol)
        {
            var docRef = _firestore.Collection(CollectionName).Document(rol.Codigo);
            try
            {
                await docRef.CreateAsync(rol);
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists)
            {
                throw new RolYaExisteException(rol.Codigo);
            }
        }

        public async Task<Rol?> GetByCodigoAsync(string codigo)
        {
            var snapshot = await _firestore.Collection(CollectionName).Document(codigo).GetSnapshotAsync();
            return snapshot.Exists ? snapshot.ConvertTo<Rol>() : null;
        }

        public Task AssignAccionAsync(string rolCodigo, string accionCodigo, string assignedBy)
        {
            var asignacion = new RolAccionAsignacion
            {
                AccionCodigo = accionCodigo,
                AssignedBy = assignedBy,
            };

            var docRef = _firestore.Collection(CollectionName).Document(rolCodigo)
                .Collection(AccionesSubcollectionName).Document(accionCodigo);
            return docRef.SetAsync(asignacion);
        }

        public async Task<IReadOnlyList<string>> GetAccionCodigosAsync(string rolCodigo)
        {
            var snapshot = await _firestore.Collection(CollectionName).Document(rolCodigo)
                .Collection(AccionesSubcollectionName).GetSnapshotAsync();
            return snapshot.Documents.Select(d => d.Id).ToList();
        }

        public async Task<IReadOnlyList<Rol>> GetAllAsync()
        {
            var snapshot = await _firestore.Collection(CollectionName).GetSnapshotAsync();
            return snapshot.Documents.Select(d => d.ConvertTo<Rol>()).ToList();
        }

        public Task CrearAsync(Rol rol, SecurityAudit auditEntry)
        {
            var docRef = _firestore.Collection(CollectionName).Document(rol.Codigo);
            return _firestore.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(docRef);
                if (snapshot.Exists) throw new RolYaExisteException(rol.Codigo);

                transaction.Create(docRef, rol);
                SecurityAuditWriter.Write(_firestore, transaction, auditEntry);
            });
        }

        public Task EditarAsync(string codigo, string nombre, string descripcion, SecurityAudit auditEntry)
        {
            var docRef = _firestore.Collection(CollectionName).Document(codigo);
            return _firestore.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(docRef);
                if (!snapshot.Exists) throw new RolNoEncontradoException(codigo);

                var actual = snapshot.ConvertTo<Rol>();
                if (actual.Nombre == nombre && actual.Descripcion == descripcion)
                {
                    // No-op idempotente: mismos valores, no hay mutación real que auditar.
                    return;
                }

                transaction.Update(docRef, new Dictionary<string, object>
                {
                    { nameof(Rol.Nombre), nombre },
                    { nameof(Rol.Descripcion), descripcion },
                    { nameof(Rol.UpdatedAt), DateTime.UtcNow },
                });
                SecurityAuditWriter.Write(_firestore, transaction, auditEntry);
            });
        }

        public Task SetActivoAsync(string codigo, bool activo, SecurityAudit auditEntry)
        {
            var docRef = _firestore.Collection(CollectionName).Document(codigo);
            return _firestore.RunTransactionAsync(async transaction =>
            {
                var snapshot = await transaction.GetSnapshotAsync(docRef);
                if (!snapshot.Exists) throw new RolNoEncontradoException(codigo);

                if (snapshot.ConvertTo<Rol>().Activo == activo)
                {
                    // No-op idempotente: ya está en el estado pedido, no corresponde ni tocar
                    // metadata ni evaluar el guard del último Administrador.
                    return;
                }

                if (!activo && codigo == UltimoAdministradorGuard.RolAdministrador)
                {
                    var efectivos = await UltimoAdministradorGuard.ContarEfectivosAsync(
                        transaction, _firestore, rolAdministradorForzadoActivo: false);
                    if (efectivos == 0) throw new UltimoAdministradorException();
                }

                transaction.Update(docRef, new Dictionary<string, object>
                {
                    { nameof(Rol.Activo), activo },
                    { nameof(Rol.UpdatedAt), DateTime.UtcNow },
                });
                SecurityAuditWriter.Write(_firestore, transaction, auditEntry);
            });
        }

        public Task AsignarAccionAsync(string rolCodigo, string accionCodigo, string assignedBy, SecurityAudit auditEntry)
        {
            var rolRef = _firestore.Collection(CollectionName).Document(rolCodigo);
            var accionRef = _firestore.Collection(AccionesCollectionName).Document(accionCodigo);
            var asignacionRef = rolRef.Collection(AccionesSubcollectionName).Document(accionCodigo);

            return _firestore.RunTransactionAsync(async transaction =>
            {
                var rolSnapshot = await transaction.GetSnapshotAsync(rolRef);
                if (!rolSnapshot.Exists) throw new RolNoEncontradoException(rolCodigo);

                var accionSnapshot = await transaction.GetSnapshotAsync(accionRef);
                if (!accionSnapshot.Exists) throw new AccionNoEncontradaException(accionCodigo);

                var asignacionSnapshot = await transaction.GetSnapshotAsync(asignacionRef);
                if (asignacionSnapshot.Exists)
                {
                    // No-op idempotente: ya estaba asignada. No se toca AssignedAt/AssignedBy ni
                    // se audita una mutación que no ocurrió.
                    return;
                }

                transaction.Create(asignacionRef, new RolAccionAsignacion { AccionCodigo = accionCodigo, AssignedBy = assignedBy });
                SecurityAuditWriter.Write(_firestore, transaction, auditEntry);
            });
        }

        public Task QuitarAccionAsync(string rolCodigo, string accionCodigo, SecurityAudit auditEntry)
        {
            var rolRef = _firestore.Collection(CollectionName).Document(rolCodigo);
            var accionRef = _firestore.Collection(AccionesCollectionName).Document(accionCodigo);
            var asignacionRef = rolRef.Collection(AccionesSubcollectionName).Document(accionCodigo);

            return _firestore.RunTransactionAsync(async transaction =>
            {
                var rolSnapshot = await transaction.GetSnapshotAsync(rolRef);
                if (!rolSnapshot.Exists) throw new RolNoEncontradoException(rolCodigo);

                var accionSnapshot = await transaction.GetSnapshotAsync(accionRef);
                if (!accionSnapshot.Exists) throw new AccionNoEncontradaException(accionCodigo);

                var asignacionSnapshot = await transaction.GetSnapshotAsync(asignacionRef);
                if (!asignacionSnapshot.Exists)
                {
                    // No-op idempotente: la Accion existe en el catálogo pero nunca estuvo (o ya
                    // no está) asignada a este Rol. Nada que borrar, nada que auditar.
                    return;
                }

                transaction.Delete(asignacionRef);
                SecurityAuditWriter.Write(_firestore, transaction, auditEntry);
            });
        }

        public Task EliminarAsync(string codigo, SecurityAudit auditEntry)
        {
            var rolRef = _firestore.Collection(CollectionName).Document(codigo);
            var accionesRef = rolRef.Collection(AccionesSubcollectionName);

            return _firestore.RunTransactionAsync(async transaction =>
            {
                var rolSnapshot = await transaction.GetSnapshotAsync(rolRef);
                if (!rolSnapshot.Exists) throw new RolNoEncontradoException(codigo);

                if (RolesEsenciales.EsEsencial(codigo)) throw new RolProtegidoException(codigo);

                if (rolSnapshot.ConvertTo<Rol>().Activo) throw new RolDebeEstarInactivoException(codigo);

                // Sin filtro de Activo -a diferencia de GetUsuarioIdsConRolActivoAsync/
                // UltimoAdministradorGuard-: una asignación UsuarioRol INACTIVA también debe
                // bloquear la baja física (condición #4 de docs/api-mvp-plan.md §12), no solo una
                // activa. Este mismo read, hecho DENTRO de la transacción, es lo que le permite a
                // Firestore detectar y reintentar/abortar si una asignación nueva aparece
                // concurrentemente antes del commit.
                var asignacionesQuery = _firestore.CollectionGroup(UsuarioRolesSubcollectionName);
                var asignacionesSnapshot = await transaction.GetSnapshotAsync(asignacionesQuery);
                var tieneAsignacion = asignacionesSnapshot.Documents
                    .Where(d => d.Id == codigo)
                    .Select(d => d.Reference.Parent.Parent)
                    .Any(usuarioRef => usuarioRef != null && usuarioRef.Parent.Id == UsuariosCollectionName);
                if (tieneAsignacion) throw new RolTieneUsuariosAsignadosException(codigo);

                var accionesSnapshot = await transaction.GetSnapshotAsync(accionesRef);
                foreach (var accionDoc in accionesSnapshot.Documents)
                {
                    transaction.Delete(accionDoc.Reference);
                }

                transaction.Delete(rolRef);
                SecurityAuditWriter.Write(_firestore, transaction, auditEntry);
            });
        }
    }
}
