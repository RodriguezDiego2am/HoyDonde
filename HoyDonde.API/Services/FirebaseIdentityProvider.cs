using System.Threading.Tasks;
using FirebaseAdmin.Auth;
using HoyDonde.API.Exceptions;

namespace HoyDonde.API.Services
{
    public class FirebaseIdentityProvider : IIdentityProvider
    {
        public const string ProviderName = "FIREBASE";

        public async Task<IdentityCreationResult> CreateIdentityAsync(string email, string password, string? displayName = null)
        {
            var args = new UserRecordArgs
            {
                Email = email,
                Password = password,
                DisplayName = displayName
            };

            try
            {
                var userRecord = await FirebaseAuth.DefaultInstance.CreateUserAsync(args);
                return new IdentityCreationResult(userRecord.Uid, ProviderName);
            }
            catch (FirebaseAuthException ex) when (ex.AuthErrorCode == AuthErrorCode.EmailAlreadyExists)
            {
                throw new IdentityEmailAlreadyExistsException(email);
            }
        }

        public Task DeleteIdentityAsync(string externalSubjectId)
        {
            return FirebaseAuth.DefaultInstance.DeleteUserAsync(externalSubjectId);
        }

        public Task SetActiveAsync(string externalSubjectId, bool isActive)
        {
            var args = new UserRecordArgs
            {
                Uid = externalSubjectId,
                Disabled = !isActive
            };
            return FirebaseAuth.DefaultInstance.UpdateUserAsync(args);
        }

        public Task UpdateAttributesAsync(string externalSubjectId, IdentityAttributeUpdate update)
        {
            var args = new UserRecordArgs { Uid = externalSubjectId };
            if (update.Email != null) args.Email = update.Email;
            if (update.DisplayName != null) args.DisplayName = update.DisplayName;
            if (update.PhoneNumber != null) args.PhoneNumber = update.PhoneNumber;
            return FirebaseAuth.DefaultInstance.UpdateUserAsync(args);
        }

        public async Task<string> GeneratePasswordResetLinkAsync(string externalSubjectId)
        {
            var userRecord = await FirebaseAuth.DefaultInstance.GetUserAsync(externalSubjectId);
            return await FirebaseAuth.DefaultInstance.GeneratePasswordResetLinkAsync(userRecord.Email);
        }
    }
}
