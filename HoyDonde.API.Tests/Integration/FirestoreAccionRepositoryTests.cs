using System;
using System.Threading.Tasks;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    [Collection(FirestoreEmulatorCollection.Name)]
    public class FirestoreAccionRepositoryTests : IAsyncLifetime
    {
        private readonly FirestoreEmulatorFixture _fixture;
        private readonly string _codigo = $"TEST_ACCION_{Guid.NewGuid():N}";
        private FirestoreAccionRepository? _sut;

        public FirestoreAccionRepositoryTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
            if (_fixture.Db != null)
            {
                _sut = new FirestoreAccionRepository(_fixture.Db);
            }
        }

        public Task InitializeAsync() => Task.CompletedTask;

        public async Task DisposeAsync()
        {
            if (_fixture.Db == null) return;
            await _fixture.Db.Collection("acciones").Document(_codigo).DeleteAsync();
        }

        [FirestoreEmulatorFact]
        public async Task CreateAsync_PersistsAccionWithCodigoAsDocumentId()
        {
            await _sut!.CreateAsync(new Accion { Codigo = _codigo, Descripcion = "Accion de prueba" });

            var accion = await _sut.GetByCodigoAsync(_codigo);

            Assert.NotNull(accion);
            Assert.Equal(_codigo, accion!.Codigo);
            Assert.True(accion.Activo);
        }

        [FirestoreEmulatorFact]
        public async Task CreateAsync_DuplicateCodigo_ThrowsAccionYaExisteException()
        {
            await _sut!.CreateAsync(new Accion { Codigo = _codigo, Descripcion = "Accion de prueba" });

            await Assert.ThrowsAsync<AccionYaExisteException>(() =>
                _sut.CreateAsync(new Accion { Codigo = _codigo, Descripcion = "Otra" }));
        }
    }
}
