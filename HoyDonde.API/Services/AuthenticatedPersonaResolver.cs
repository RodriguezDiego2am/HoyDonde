using HoyDonde.API.Exceptions;
using HoyDonde.API.Repositories;
using System.Threading.Tasks;

namespace HoyDonde.API.Services
{
    public class AuthenticatedPersonaResolver : IAuthenticatedPersonaResolver
    {
        private readonly IUsuarioRepository _usuarioRepository;

        public AuthenticatedPersonaResolver(IUsuarioRepository usuarioRepository)
        {
            _usuarioRepository = usuarioRepository;
        }

        public async Task<string> ResolvePersonaIdAsync(string externalSubjectId)
        {
            var usuarioId = await _usuarioRepository.GetUsuarioIdByExternalSubjectAsync(
                FirebaseIdentityProvider.ProviderName, externalSubjectId);
            if (usuarioId == null)
                throw new IdentityNotProvisionedException(externalSubjectId);

            var usuario = await _usuarioRepository.GetByIdAsync(usuarioId);
            if (usuario == null)
                throw new IdentityNotProvisionedException(externalSubjectId);

            return usuario.PersonaId;
        }
    }
}
