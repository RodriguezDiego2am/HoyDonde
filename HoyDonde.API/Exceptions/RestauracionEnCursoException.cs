using System;

namespace HoyDonde.API.Exceptions
{
    // Una importación administrada sobrescribe datos de producción: nunca puede haber dos
    // restauraciones sin terminar a la vez, aunque dos administradores pidan restaurar al mismo
    // tiempo copias distintas.
    public class RestauracionEnCursoException : Exception
    {
        public RestauracionEnCursoException()
            : base("Ya hay una restauración en curso. Esperá a que termine antes de iniciar otra.")
        {
        }
    }
}
