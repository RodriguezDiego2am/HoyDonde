using System;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // Provee un FirestoreDb apuntando al Firestore Emulator cuando FIRESTORE_EMULATOR_HOST
    // está configurado. FirestoreDb.Create(projectId) NO detecta el emulador por sí solo
    // -su EmulatorDetection por defecto es None y construye credenciales reales-, por eso acá
    // se arma con FirestoreDbBuilder y EmulatorDetection.EmulatorOrProduction, que sí lee esa
    // variable de entorno. Si no está configurada, Db queda en null y los tests decorados con
    // [FirestoreEmulatorFact] se saltan antes de construir la clase de test.
    public class FirestoreEmulatorFixture
    {
        public FirestoreDb? Db { get; }

        public FirestoreEmulatorFixture()
        {
            var emulatorHost = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
            Db = string.IsNullOrWhiteSpace(emulatorHost)
                ? null
                : new FirestoreDbBuilder
                {
                    ProjectId = "hoydonde-security-refactor-tests",
                    EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
                }.Build();
        }
    }

    [CollectionDefinition(Name)]
    public class FirestoreEmulatorCollection : ICollectionFixture<FirestoreEmulatorFixture>
    {
        public const string Name = "FirestoreEmulator";
    }
}
