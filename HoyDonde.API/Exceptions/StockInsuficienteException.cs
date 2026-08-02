using System;

namespace HoyDonde.API.Exceptions
{
    // No hay stock suficiente para la cantidad solicitada (docs/api-mvp-plan.md §3).
    // Se mapea a HTTP 409.
    public class StockInsuficienteException : Exception
    {
        public StockInsuficienteException(string ticketTypeId, int disponible, int solicitado)
            : base($"Stock insuficiente para el tipo de ticket '{ticketTypeId}'. Disponible: {disponible}, solicitado: {solicitado}.")
        {
        }
    }
}
