using System;

namespace HoyDonde.API.Exceptions
{
    // Solo una exportación en estado Succeeded puede usarse como origen de una restauración.
    public class BackupNoRestaurableException : Exception
    {
        public BackupNoRestaurableException()
            : base("La copia seleccionada no está disponible para restaurar.")
        {
        }
    }
}
