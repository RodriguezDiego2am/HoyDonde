using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using HoyDonde.API.Models;

namespace HoyDonde.API.DTOs
{
    // Campos editables por el organizador dueño del evento, solo mientras el evento está en
    // Borrador (docs/api-mvp-plan.md §2). Deliberadamente no incluye Estado ni
    // OrganizadorPersonaId: el ciclo de estados y la propiedad del evento no se tocan acá.
    // TicketGroups reemplaza la colección completa de tipos de ticket (sin edición incremental
    // por id); a diferencia de la creación, no exige al menos un elemento: PublishEventAsync es
    // quien rechaza publicar un evento sin tipos de ticket.
    public class EventUpdateRequest : IValidatableObject
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "El nombre del evento es obligatorio.")]
        public string Nombre { get; set; } = string.Empty;

        public string Descripcion { get; set; } = string.Empty;

        public DateTime FechaInicio { get; set; }

        public DateTime FechaFin { get; set; }

        [Required(AllowEmptyStrings = false, ErrorMessage = "La ubicación del evento es obligatoria.")]
        public string Ubicacion { get; set; } = string.Empty;

        public Event.EventCategory Categoria { get; set; }

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
