using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using Xunit;

namespace HoyDonde.API.Tests
{
    // Validaciones de DataAnnotations en los DTOs de Event (docs/api-mvp-plan.md §2), primera
    // línea de defensa antes del pipeline HTTP. EventService (ver EventServiceEmulatorTests)
    // repite las mismas reglas de forma independiente, porque estos DTOs solo se validan
    // automáticamente dentro de ASP.NET Core (model binding), no cuando el servicio se llama
    // directamente.
    public class EventDtoValidationTests
    {
        private static IList<ValidationResult> Validate(object instance)
        {
            var context = new ValidationContext(instance);
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(instance, context, results, validateAllProperties: true);

            if (instance is IValidatableObject validatable)
            {
                results.AddRange(validatable.Validate(context));
            }

            return results;
        }

        private static EventCreateRequest ValidCreateRequest() => new EventCreateRequest
        {
            Nombre = "Festival de prueba",
            Descripcion = "Descripcion",
            Ubicacion = "La Plaza",
            FechaInicio = DateTime.UtcNow.AddDays(5),
            FechaFin = DateTime.UtcNow.AddDays(6),
            Categoria = Event.EventCategory.Musica,
            TicketGroups = new List<TicketGroupDto>
            {
                new TicketGroupDto { Nombre = "General", Precio = 50, CantidadDisponible = 100 }
            }
        };

        [Fact]
        public void EventCreateRequest_Valid_HasNoErrors()
        {
            Assert.Empty(Validate(ValidCreateRequest()));
        }

        [Fact]
        public void EventCreateRequest_NombreVacio_IsInvalid()
        {
            var request = ValidCreateRequest();
            request.Nombre = "";

            Assert.NotEmpty(Validate(request));
        }

        [Fact]
        public void EventCreateRequest_UbicacionVacia_IsInvalid()
        {
            var request = ValidCreateRequest();
            request.Ubicacion = "";

            Assert.NotEmpty(Validate(request));
        }

        [Fact]
        public void EventCreateRequest_SinTicketGroups_IsInvalid()
        {
            var request = ValidCreateRequest();
            request.TicketGroups = new List<TicketGroupDto>();

            Assert.NotEmpty(Validate(request));
        }

        [Fact]
        public void EventCreateRequest_FechaInicioPasada_IsInvalid()
        {
            var request = ValidCreateRequest();
            request.FechaInicio = DateTime.UtcNow.AddDays(-1);

            Assert.Contains(Validate(request), r => r.MemberNames.Contains(nameof(EventCreateRequest.FechaInicio)));
        }

        [Fact]
        public void EventCreateRequest_FechaFinAntesDeFechaInicio_IsInvalid()
        {
            var request = ValidCreateRequest();
            request.FechaInicio = DateTime.UtcNow.AddDays(5);
            request.FechaFin = DateTime.UtcNow.AddDays(4);

            Assert.Contains(Validate(request), r => r.MemberNames.Contains(nameof(EventCreateRequest.FechaFin)));
        }

        [Fact]
        public void EventUpdateRequest_SinTicketGroups_IsValid()
        {
            // A diferencia de EventCreateRequest, la ausencia de tipos de ticket es válida al
            // editar un Borrador: PublishEventAsync es quien lo rechaza más adelante
            // (docs/api-mvp-plan.md §2).
            var request = new EventUpdateRequest
            {
                Nombre = "Festival editado",
                Ubicacion = "Nueva ubicacion",
                FechaInicio = DateTime.UtcNow.AddDays(5),
                FechaFin = DateTime.UtcNow.AddDays(6),
                Categoria = Event.EventCategory.Musica,
                TicketGroups = new List<TicketGroupDto>()
            };

            Assert.Empty(Validate(request));
        }

        [Fact]
        public void EventUpdateRequest_FechaFinAntesDeFechaInicio_IsInvalid()
        {
            var request = new EventUpdateRequest
            {
                Nombre = "Festival editado",
                Ubicacion = "Nueva ubicacion",
                FechaInicio = DateTime.UtcNow.AddDays(5),
                FechaFin = DateTime.UtcNow.AddDays(4),
            };

            Assert.Contains(Validate(request), r => r.MemberNames.Contains(nameof(EventUpdateRequest.FechaFin)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void TicketGroupDto_NombreVacio_IsInvalid(string nombre)
        {
            var group = new TicketGroupDto { Nombre = nombre, Precio = 10, CantidadDisponible = 1 };

            Assert.NotEmpty(Validate(group));
        }

        [Fact]
        public void TicketGroupDto_PrecioNegativo_IsInvalid()
        {
            var group = new TicketGroupDto { Nombre = "General", Precio = -1, CantidadDisponible = 1 };

            Assert.NotEmpty(Validate(group));
        }

        [Fact]
        public void TicketGroupDto_PrecioCero_IsValid()
        {
            // Entradas gratuitas permitidas (docs/api-mvp-plan.md §0, decisión 7).
            var group = new TicketGroupDto { Nombre = "Gratis", Precio = 0, CantidadDisponible = 1 };

            Assert.Empty(Validate(group));
        }

        [Fact]
        public void TicketGroupDto_CantidadMenorQueUno_IsInvalid()
        {
            var group = new TicketGroupDto { Nombre = "General", Precio = 10, CantidadDisponible = 0 };

            Assert.NotEmpty(Validate(group));
        }
    }
}
