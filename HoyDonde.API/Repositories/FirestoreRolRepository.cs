using Google.Cloud.Firestore;
using Grpc.Core;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
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
    }
}
