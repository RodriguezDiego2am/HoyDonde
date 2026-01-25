namespace HoyDonde.API.Models
{
    public class ReservationItem
    {
        public int Id { get; set; }
        public int ReservationId { get; set; }
        public virtual Reservation Reservation { get; set; } = null!;

        public int TicketTypeId { get; set; }
        public virtual TicketType TicketType { get; set; } = null!;

        public int Cantidad { get; set; }
    }
}
