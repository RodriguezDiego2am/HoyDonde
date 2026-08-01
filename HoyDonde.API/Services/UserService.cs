using FirebaseAdmin.Auth;
using HoyDonde.API.Exceptions;
using HoyDonde.API.Models;
using HoyDonde.API.Repositories;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    public class UserService : IUserService
    {
        private readonly IUserRepository _userRepository;
        private readonly IEventService _eventService;

        public UserService(IUserRepository userRepository, IEventService eventService)
        {
            _userRepository = userRepository;
            _eventService = eventService;
        }

        // Este endpoint (POST /api/users/admin) requiere rol Admin autenticado (ver UserController).
        // La creación del primer administrador queda fuera de esta API: es una operación de
        // bootstrap administrada por separado (p. ej. asignar el custom claim "role"="Admin" y
        // el documento en Firestore de forma manual/con un script one-off fuera del repo), no un
        // flujo expuesto por HTTP. Por eso ya no depende de IsAdminCreatedAsync, que era un
        // placeholder que siempre devolvía false y no protegía nada.
        public async Task<bool> RegisterAdminAsync(string email, string password)
        {
            var userRecordArgs = new UserRecordArgs
            {
                Email = email,
                Password = password,
            };

            var userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(userRecordArgs);
            var claims = new Dictionary<string, object>() { { "role", Roles.Admin } };
            await FirebaseAuth.DefaultInstance.SetCustomUserClaimsAsync(userRecord.Uid, claims);

            var admin = new Admin 
            { 
                Id = userRecord.Uid, 
                UserName = email, 
                Email = email,
                Role = Roles.Admin,
            };
            await _userRepository.CreateUserAsync(admin);
            return true;
        }

        public async Task<bool> RegisterOrganizadorAsync(string email, string password)
        {
            var userRecordArgs = new UserRecordArgs
            {
                Email = email,
                Password = password,
            };

            var userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(userRecordArgs);
            var claims = new Dictionary<string, object>() { { "role", Roles.Organizador } };
            await FirebaseAuth.DefaultInstance.SetCustomUserClaimsAsync(userRecord.Uid, claims);

            var organizador = new Organizador 
            { 
                Id = userRecord.Uid, 
                UserName = email, 
                Email = email,
                Role = Roles.Organizador,
            };
            await _userRepository.CreateUserAsync(organizador);
            return true;
        }

        public async Task<bool> RegisterClienteAsync(string email, string password, string fullName, string dni, string phoneNumber)
        {
            // Phone numbers in Firebase Auth must be cleanly formatted E.164, 
            // so we set it in Firestore but omit from Auth if not strict.
            var userRecordArgs = new UserRecordArgs
            {
                Email = email,
                Password = password,
                DisplayName = fullName
            };

            var userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(userRecordArgs);
            var claims = new Dictionary<string, object>() { { "role", Roles.Cliente } };
            await FirebaseAuth.DefaultInstance.SetCustomUserClaimsAsync(userRecord.Uid, claims);

            var cliente = new Cliente 
            { 
                Id = userRecord.Uid, 
                UserName = email, 
                Email = email, 
                FullName = fullName, 
                DNI = dni, 
                PhoneNumber = phoneNumber,
                Role = Roles.Cliente,
            };
            await _userRepository.CreateUserAsync(cliente);
            return true;
        }

        public async Task<bool> RegisterControlAsync(string userName, string password, string eventId, string organizadorId)
        {
            // El evento se lee de Firestore y se compara contra el organizadorId del token
            // (nunca contra un valor enviado por el cliente). Si el evento no existe o
            // pertenece a otro organizador, no se crea nada en Firebase Auth ni en Firestore.
            var evento = await _eventService.GetByIdAsync(eventId);
            if (evento == null) throw new EventNotFoundException(eventId);
            if (evento.OrganizadorId != organizadorId) throw new EventOwnershipException(eventId, organizadorId);

            var userEmail = $"{userName}@control.hoydonde.com";
            var userRecordArgs = new UserRecordArgs
            {
                Email = userEmail,
                Password = password,
                DisplayName = userName
            };

            var userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(userRecordArgs);
            var claims = new Dictionary<string, object>() { { "role", Roles.Control } };
            await FirebaseAuth.DefaultInstance.SetCustomUserClaimsAsync(userRecord.Uid, claims);

            var control = new Control
            {
                Id = userRecord.Uid,
                UserName = userName,
                Email = userEmail,
                EventId = eventId,
                OrganizadorId = organizadorId,
                Role = Roles.Control,
            };
            await _userRepository.CreateUserAsync(control);
            return true;
        }
    }
}
