using System.Collections.Generic;
using System.Threading.Tasks;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;

namespace HoyDonde.API.Services
{
    public interface ITicketService
    {
        Task<CompraResponseDto> BuyTicketsAsync(string clienteId, TicketBuyRequest request);
        Task<List<TicketResponseDto>> GetTicketsByClienteIdAsync(string clienteId);
        Task<TicketValidationOutcome> ValidateTicketAsync(string controlId, string ticketId, string eventId);
    }
}
