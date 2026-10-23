using System.Threading.Tasks;
using HoyDonde.API.DTOs;

namespace HoyDonde.API.Services
{
    public interface ITicketTransferService
    {
        Task<TicketTransferResponseDto> TransferAsync(string actorUid, string ticketId, string recipientEmail);
    }
}
