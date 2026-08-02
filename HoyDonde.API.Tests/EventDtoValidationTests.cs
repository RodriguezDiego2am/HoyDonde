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

    // Contrato de salida de tipos de ticket (corrección previa al cierre de API-MVP 4):
    // TicketGroupDto es exclusivamente de ENTRADA (EventCreateRequest/EventUpdateRequest);
    // TicketTypeResponseDto es exclusivamente de SALIDA (EventResponse), con el TicketTypeId real
    // generado por el servidor. Ningún modelo de persistencia (Event/TicketType) debe quedar
    // expuesto en ninguno de los dos.
    public class TicketTypeContractShapeTests
    {
        [Fact]
        public void TicketGroupDto_NeverAcceptsAnIdFromTheClient()
        {
            // Un DTO de entrada con un campo "Id" permitiría a un cliente intentar controlar un
            // identificador persistido; TicketGroupDto no debe tener esa propiedad en absoluto.
            var idProperty = typeof(TicketGroupDto).GetProperty("Id");

            Assert.Null(idProperty);
        }

        [Fact]
        public void EventResponse_TicketGroups_UsesTicketTypeResponseDto_NotTheInputDto()
        {
            var ticketGroupsProperty = typeof(EventResponse).GetProperty(nameof(EventResponse.TicketGroups));
            Assert.NotNull(ticketGroupsProperty);

            var itemType = ticketGroupsProperty!.PropertyType.GetGenericArguments().Single();

            Assert.Equal(typeof(TicketTypeResponseDto), itemType);
            Assert.NotEqual(typeof(TicketGroupDto), itemType);
        }

        [Fact]
        public void TicketTypeResponseDto_ExposesIdNombrePrecioYCantidadDisponible()
        {
            var type = typeof(TicketTypeResponseDto);

            Assert.NotNull(type.GetProperty("Id"));
            Assert.NotNull(type.GetProperty("Nombre"));
            Assert.NotNull(type.GetProperty("Precio"));
            Assert.NotNull(type.GetProperty("CantidadDisponible"));
        }

        [Fact]
        public void EventCreateRequest_And_EventUpdateRequest_NeverRequireOrAcceptATicketTypeId()
        {
            // El request de creación/edición se arma exclusivamente con TicketGroupDto (sin Id);
            // esto confirma en tiempo de ejecución que ninguno de los dos requests expone -ni
            // necesita- un campo de id para sus tipos de ticket.
            var createTicketGroupsType = typeof(EventCreateRequest).GetProperty(nameof(EventCreateRequest.TicketGroups))!
                .PropertyType.GetGenericArguments().Single();
            var updateTicketGroupsType = typeof(EventUpdateRequest).GetProperty(nameof(EventUpdateRequest.TicketGroups))!
                .PropertyType.GetGenericArguments().Single();

            Assert.Equal(typeof(TicketGroupDto), createTicketGroupsType);
            Assert.Equal(typeof(TicketGroupDto), updateTicketGroupsType);
            Assert.Null(createTicketGroupsType.GetProperty("Id"));
            Assert.Null(updateTicketGroupsType.GetProperty("Id"));
        }

        [Fact]
        public void EventResponse_And_TicketTypeResponseDto_NeverExposeTheFirestorePersistenceModels()
        {
            // Ningún DTO de salida de Event debe tener una propiedad cuyo TIPO sea el modelo de
            // persistencia (Event/TicketType), ni siquiera indirectamente vía una colección de ese
            // tipo -eso sería serializar el documento de Firestore tal cual por HTTP.
            AssertNoPropertyOfType(typeof(EventResponse), typeof(Event));
            AssertNoPropertyOfType(typeof(EventResponse), typeof(TicketType));
            AssertNoPropertyOfType(typeof(TicketTypeResponseDto), typeof(Event));
            AssertNoPropertyOfType(typeof(TicketTypeResponseDto), typeof(TicketType));
        }

        private static void AssertNoPropertyOfType(Type dtoType, Type forbiddenModelType)
        {
            foreach (var property in dtoType.GetProperties())
            {
                var propertyType = property.PropertyType;
                var elementType = propertyType.IsGenericType
                    ? propertyType.GetGenericArguments().FirstOrDefault()
                    : null;

                Assert.False(propertyType == forbiddenModelType,
                    $"{dtoType.Name}.{property.Name} expone el modelo de persistencia {forbiddenModelType.Name} directamente.");
                Assert.False(elementType == forbiddenModelType,
                    $"{dtoType.Name}.{property.Name} expone una colección del modelo de persistencia {forbiddenModelType.Name}.");
            }
        }
    }
}
