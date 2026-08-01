using System;
using Google.Cloud.Firestore;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // Provee un FirestoreDb apuntando al Firestore Emulator cuando FIRESTORE_EMULATOR_HOST
    // está configurado (el cliente de Google.Cloud.Firestore lo detecta automáticamente y no
    // requiere credenciales reales). Si no está configurado, Db queda en null y los tests
    // decorados con [FirestoreEmulatorFact] se saltan antes de construir la clase de test.
    public class FirestoreEmulatorFixture
    {
        public FirestoreDb? Db { get; }

        public FirestoreEmulatorFixture()
        {
            var emulatorHost = Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST");
            Db = string.IsNullOrWhiteSpace(emulatorHost)
                ? null
                : FirestoreDb.Create("hoydonde-security-refactor-tests");
        }
    }

    [CollectionDefinition(Name)]
    public class FirestoreEmulatorCollection : ICollectionFixture<FirestoreEmulatorFixture>
    {
        public const string Name = "FirestoreEmulator";
    }
}
