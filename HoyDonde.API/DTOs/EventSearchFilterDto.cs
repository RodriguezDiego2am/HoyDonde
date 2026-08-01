using System;

namespace HoyDonde.API.DTOs
{
    public class EventSearchFilterDto
    {
        public string? Categoria { get; set; }
        public string? Ubicacion { get; set; }
        public DateTime? FechaInicio { get; set; }
        
        // Parámetros de paginación para Firebase
        public int Limit { get; set; } = 20;
        public string? LastEventId { get; set; }
    }
}
