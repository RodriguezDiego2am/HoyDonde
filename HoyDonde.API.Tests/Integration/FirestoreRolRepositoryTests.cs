using System;
using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    [Collection(FirestoreEmulatorCollection.Name)]
    public class FirestoreRolRepositoryTests : IAsyncLifetime
    {
        private readonly FirestoreEmulatorFixture _fixture;
        private readonly string _codigo = $"TEST_ROL_{Guid.NewGuid():N}";
        private FirestoreRolRepository? _sut;

        public FirestoreRolRepositoryTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
            if (_fixture.Db != null)
            {
                _sut = new FirestoreRolRepository(_fixture.Db);
            }
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            if (_fixture.Db == null) return;

            var docRef = _fixture.Db.Collection("roles").Document(_codigo);
            var acciones = await docRef.Collection("acciones").GetSnapshotAsync();
            foreach (var doc in acciones.Documents)
            {
                await doc.Reference.DeleteAsync();
            }
            await docRef.DeleteAsync();
        }

        [FirestoreEmulatorFact]
        public async Task CreateAsync_PersistsRolWithCodigoAsDocumentId()
        {
            await _sut!.CreateAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "Rol de prueba" });

            var rol = await _sut.GetByCodigoAsync(_codigo);

            Assert.NotNull(rol);
            Assert.Equal(_codigo, rol!.Codigo);
            Assert.Equal("Test", rol.Nombre);
            Assert.True(rol.Activo);
        }

        [FirestoreEmulatorFact]
        public async Task CreateAsync_DuplicateCodigo_ThrowsRolYaExisteException()
        {
            await _sut!.CreateAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "Rol de prueba" });

            await Assert.ThrowsAsync<RolYaExisteException>(() =>
                _sut.CreateAsync(new Rol { Codigo = _codigo, Nombre = "Otro nombre", Descripcion = "Otro" }));
        }

        [FirestoreEmulatorFact]
        public async Task AssignAccionAsync_IsIdempotent_WhenCalledTwice()
        {
            await _sut!.CreateAsync(new Rol { Codigo = _codigo, Nombre = "Test", Descripcion = "Rol de prueba" });

            await _sut.AssignAccionAsync(_codigo, "ACCION_X", "tester");
            await _sut.AssignAccionAsync(_codigo, "ACCION_X", "tester");

            var acciones = await _sut.GetAccionCodigosAsync(_codigo);

            Assert.Single(acciones);
            Assert.Equal("ACCION_X", acciones[0]);
        }

        [FirestoreEmulatorFact]
        public async Task GetByCodigoAsync_ReturnsNull_WhenRolDoesNotExist()
        {
            var rol = await _sut!.GetByCodigoAsync($"NO_EXISTE_{Guid.NewGuid():N}");

            Assert.Null(rol);
        }
    }
}
