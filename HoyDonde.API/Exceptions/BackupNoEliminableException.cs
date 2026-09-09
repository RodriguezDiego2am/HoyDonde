using System;

namespace HoyDonde.API.Exceptions
{
    // Una copia en curso (Pending/Running) y toda importación quedan fuera del borrado: solo se
    // elimina una exportación ya terminal.
    public class BackupNoEliminableException : Exception
    {
        public BackupNoEliminableException()
            : base("La copia seleccionada no se puede eliminar.")
        {
        }
    }
}
