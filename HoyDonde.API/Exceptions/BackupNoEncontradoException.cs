using System;

namespace HoyDonde.API.Exceptions
{
    // Copias administradas de Firestore: la copia referenciada no existe en el historial.
    public class BackupNoEncontradoException : Exception
    {
        public BackupNoEncontradoException()
            : base("La copia indicada no existe.")
        {
        }
    }
}
