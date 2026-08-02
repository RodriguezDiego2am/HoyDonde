using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using HoyDonde.API.Models;

namespace HoyDonde.API.DTOs
{
    public class EventCreateRequest : IValidatableObject
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "El nombre del evento es obligatorio.")]
        public string Nombre { get; set; } = string.Empty;

        public string Descripcion { get; set; } = string.Empty;

        public DateTime FechaInicio { get; set; }

        public DateTime FechaFin { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "La ubicación del evento es obligatoria.")]
        public string Ubicacion { get; set; } = string.Empty;

        public Event.EventCategory Categoria { get; set; }

        [MinLength(1, ErrorMessage = "El evento debe tener al menos un tipo de ticket.")]
        public List<TicketGroupDto> TicketGroups { get; set; } = new();

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (FechaInicio <= DateTime.UtcNow)
                yield return new ValidationResult("La fecha de inicio debe ser futura.", new[] { nameof(FechaInicio) });

            if (FechaFin <= FechaInicio)
                yield return new ValidationResult("La fecha de fin debe ser posterior a la fecha de inicio.", new[] { nameof(FechaFin) });
        }
    }
}
