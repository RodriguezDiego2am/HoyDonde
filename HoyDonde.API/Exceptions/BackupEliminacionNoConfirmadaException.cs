using System;

namespace HoyDonde.API.Exceptions
{
    // El borrado destruye los archivos de la copia en Cloud Storage: exige confirmación explícita,
    // revalidada en el servicio y no solo en el cliente.
    public class BackupEliminacionNoConfirmadaException : Exception
    {
        public BackupEliminacionNoConfirmadaException()
            : base("Debés confirmar explícitamente la eliminación de la copia.")
        {
        }
    }
}
