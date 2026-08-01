using System;

namespace HoyDonde.API.Tests.Integration
{
    // Se salta automáticamente si no hay Firestore Emulator disponible (FIRESTORE_EMULATOR_HOST
    // sin configurar). Ver docs/security-refactor-plan.md §8 (capa 3: integración de
    // repositorios contra el emulador) — nunca se mockea el SDK de Firestore como sustituto.
    public sealed class FirestoreEmulatorFactAttribute : FactAttribute
    {
        public FirestoreEmulatorFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST")))
            {
                Skip = "Requiere FIRESTORE_EMULATOR_HOST (Firestore Emulator) — no disponible en este entorno.";
            }
        }
    }
}
