using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using HoyDonde.API.DTOs;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using HoyDonde.API.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace HoyDonde.API.Tests.Integration
{
    // API-MVP 4, gate de verificación (docs/api-mvp-plan.md §5, decisión 9): recorrido
    // encadenado con estado real compartido, pipeline HTTP real (TestServer de
    // WebApplicationFactory<Program>), controllers/services/repositorios reales, y Firestore
    // Emulator real. Nada de IEventService/ITicketService/IUserService mockeado: lo único
    // sustituido es el esquema de autenticación (EmulatorFakeAuthHandler), para poder simular
    // login como organizador/cliente/control sin credenciales Firebase reales.
    [Collection(FirestoreEmulatorCollection.Name)]
    public class FullJourneyEmulatorTests
    {
        private readonly FirestoreEmulatorFixture _fixture;

        public FullJourneyEmulatorTests(FirestoreEmulatorFixture fixture)
        {
            _fixture = fixture;
        }

        private async Task SeedSecurityCatalogAsync(FirestoreDb db)
        {
            var rolRepository = new FirestoreRolRepository(db);
            var accionRepository = new FirestoreAccionRepository(db);
            await new SecurityCatalogSeeder(rolRepository, accionRepository).SeedAsync();
        }

        private async Task<(string Uid, string PersonaId, string UsuarioId)> SeedActorAsync(
            FirestoreDb db, string rolCodigo, bool activo = true)
        {
            var usuarioRepository = new FirestoreUsuarioRepository(db);
            var uid = $"uid-{rolCodigo.ToLowerInvariant()}-{Guid.NewGuid():N}";
            var personaId = $"persona-{rolCodigo.ToLowerInvariant()}-{Guid.NewGuid():N}";
            var usuarioId = $"usuario-{rolCodigo.ToLowerInvariant()}-{Guid.NewGuid():N}";

            var result = await usuarioRepository.ProvisionarAsync(new UsuarioProvisioningRequest(
                personaId, usuarioId, "FIREBASE", uid, $"{uid}@test.hoydonde.local", rolCodigo, "test-seed"));

            if (!activo)
            {
                await db.Collection("usuarios").Document(result.UsuarioId).UpdateAsync(nameof(Usuario.IsActive), false);
            }

            return (uid, result.PersonaId, result.UsuarioId);
        }

        private static HttpRequestMessage Request(HttpMethod method, string path, string uid, object? body = null)
        {
            var msg = new HttpRequestMessage(method, path);
            msg.Headers.Authorization = new AuthenticationHeaderValue("Test");
            msg.Headers.Add("Test-Uid", uid);
            if (body != null) msg.Content = JsonContent.Create(body);
            return msg;
        }

        [FirestoreEmulatorFact]
        public async Task FullJourney_CreatePublishBuyValidate_SecondValidationRejected_AndFirestoreStateIsCorrect()
        {
            var db = _fixture.Db!;
            await SeedSecurityCatalogAsync(db);

            var (organizadorUid, organizadorPersonaId, _) = await SeedActorAsync(db, "ORGANIZADOR");
            var (clienteUid, clientePersonaId, _) = await SeedActorAsync(db, "CLIENTE");
            var (controlUid, controlPersonaId, _) = await SeedActorAsync(db, "CONTROL");

            using var factory = new EmulatorWebApplicationFactory(db);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // 1. Organizador crea un evento válido.
            var fechaInicio = DateTime.UtcNow.AddHours(2);
            var fechaFin = DateTime.UtcNow.AddHours(4);
            var createRequest = new EventCreateRequest
            {
                Nombre = "Recorrido Integral",
                Descripcion = "Evento del gate de verificación de API-MVP 4",
                FechaInicio = fechaInicio,
                FechaFin = fechaFin,
                Ubicacion = "Estadio de Prueba",
                Categoria = Event.EventCategory.Musica,
                TicketGroups = new List<TicketGroupDto>
                {
                    new TicketGroupDto { Nombre = "General", Precio = 100m, CantidadDisponible = 3 }
                }
            };

            var createResponse = await client.SendAsync(Request(HttpMethod.Post, "/api/events", organizadorUid, createRequest));
            var createBody = await createResponse.Content.ReadAsStringAsync();
            Assert.True(createResponse.StatusCode == HttpStatusCode.OK, $"CreateEvent falló: {createBody}");
            var created = await createResponse.Content.ReadFromJsonAsync<EventResponse>();
            Assert.NotNull(created);
            var eventId = created!.Id;

            // eventId y ticketTypeId se obtienen EXCLUSIVAMENTE de la respuesta HTTP de creación
            // (EventResponse.TicketGroups es ahora TicketTypeResponseDto, con el Id real generado
            // por el servidor) — nada se lee de Firestore ni se construye internamente antes de
            // comprar: un cliente real puede ejecutar este mismo flujo usando solo el contrato
            // público.
            Assert.Single(created.TicketGroups);
            var ticketType = created.TicketGroups[0];
            Assert.False(string.IsNullOrEmpty(ticketType.Id));
            var ticketTypeId = ticketType.Id;
            var stockInicial = ticketType.CantidadDisponible;

            // 2. Organizador lo publica.
            var publishResponse = await client.SendAsync(Request(HttpMethod.Post, $"/api/events/{eventId}/publish", organizadorUid));
            Assert.Equal(HttpStatusCode.OK, publishResponse.StatusCode);

            // Precondición del recorrido (no uno de los pasos HTTP numerados): el Control ya está
            // asignado a este evento por este organizador, vía el repositorio real (mismo patrón
            // que UserServiceControlAssignmentEmulatorTests), sin pasar por el flujo de alta
            // privilegiada de Control (fuera del alcance del recorrido de Event/Ticket/Control).
            var controlAsignacionRepository = new FirestoreControlAsignacionRepository(db);
            await controlAsignacionRepository.AsignarAsync(controlPersonaId, eventId, organizadorPersonaId);

            // 3. Cliente compra un ticket.
            var buyRequest = new TicketBuyRequest { EventoId = eventId, TicketTypeId = ticketTypeId, Cantidad = 1 };
            var buyResponse = await client.SendAsync(Request(HttpMethod.Post, "/api/tickets/buy", clienteUid, buyRequest));
            var buyBody = await buyResponse.Content.ReadAsStringAsync();
            Assert.True(buyResponse.StatusCode == HttpStatusCode.OK, $"BuyTickets falló: {buyBody}");
            var tickets = await buyResponse.Content.ReadFromJsonAsync<List<TicketResponseDto>>();
            Assert.NotNull(tickets);
            Assert.Single(tickets!);
            var ticketId = tickets![0].Id;

            // 4. Control asignado valida el ticket.
            var validateResponse = await client.SendAsync(
                Request(HttpMethod.Post, $"/api/tickets/validate?ticketId={ticketId}&eventId={eventId}", controlUid));
            Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);

            // 5. Un segundo intento de validación se rechaza.
            var secondValidateResponse = await client.SendAsync(
                Request(HttpMethod.Post, $"/api/tickets/validate?ticketId={ticketId}&eventId={eventId}", controlUid));
            Assert.Equal(HttpStatusCode.Conflict, secondValidateResponse.StatusCode);

            // 6. Verificación directa en Firestore.
            var eventoSnapshotFinal = await db.Collection("events").Document(eventId).GetSnapshotAsync();
            var eventoFinal = eventoSnapshotFinal.ConvertTo<Event>();
            var ticketTypeFinal = eventoFinal.TicketTypes.Find(t => t.Id == ticketTypeId);
            Assert.NotNull(ticketTypeFinal);
            Assert.Equal(stockInicial - 1, ticketTypeFinal!.CantidadDisponible);

            var ticketSnapshot = await db.Collection("tickets").Document(ticketId).GetSnapshotAsync();
            Assert.True(ticketSnapshot.Exists);
            var ticketFinal = ticketSnapshot.ConvertTo<Ticket>();

            // Fotografía completa tomada en el momento de la compra.
            Assert.Equal("Recorrido Integral", ticketFinal.EventoNombre);
            Assert.Equal("General", ticketFinal.TicketTypeNombre);
            Assert.Equal(100m, ticketFinal.PrecioPagado);
            Assert.Equal(fechaInicio, ticketFinal.FechaInicio, TimeSpan.FromSeconds(1));
            Assert.Equal(fechaFin, ticketFinal.FechaFin, TimeSpan.FromSeconds(1));

            // ClientePersonaId/ValidadoPorPersonaId y estado histórico final.
            Assert.Equal(clientePersonaId, ticketFinal.ClientePersonaId);
            Assert.Equal(controlPersonaId, ticketFinal.ValidadoPorPersonaId);
            Assert.Equal(Ticket.TicketStatus.Usado, ticketFinal.Estado);
        }

        // Usuario desactivado (docs/api-mvp-plan.md §5): un Usuario cuyo IsActive == false nunca
        // tiene acciones efectivas -PermissionService.GetPermisosEfectivosPorUsuarioIdAsync corta
        // apenas lee Usuario.IsActive == false-, así que AccionAuthorizationHandler nunca llama a
        // context.Succeed y ASP.NET deniega por default. Se verifica acá contra un endpoint HTTP
        // real (no un test de PermissionService en aislamiento): un Organizador desactivado
        // recibe 403 al intentar crear un evento propio, con controller/servicio/repositorios
        // reales contra el emulador.
        [FirestoreEmulatorFact]
        public async Task DeactivatedUsuario_CreateEvent_ReturnsForbidden()
        {
            var db = _fixture.Db!;
            await SeedSecurityCatalogAsync(db);

            var (organizadorUid, _, _) = await SeedActorAsync(db, "ORGANIZADOR", activo: false);

            using var factory = new EmulatorWebApplicationFactory(db);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            var createRequest = new EventCreateRequest
            {
                Nombre = "Evento de usuario desactivado",
                Descripcion = "No debería poder crearse",
                FechaInicio = DateTime.UtcNow.AddDays(1),
                FechaFin = DateTime.UtcNow.AddDays(2),
                Ubicacion = "Cualquier lugar",
                Categoria = Event.EventCategory.Otros,
                TicketGroups = new List<TicketGroupDto>
                {
                    new TicketGroupDto { Nombre = "General", Precio = 10m, CantidadDisponible = 1 }
                }
            };

            var response = await client.SendAsync(Request(HttpMethod.Post, "/api/events", organizadorUid, createRequest));

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }
}
