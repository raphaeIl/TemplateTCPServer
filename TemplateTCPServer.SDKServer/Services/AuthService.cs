using TemplateTCPServer.Data.Repositories;

namespace TemplateTCPServer.SDKServer.Services
{
    public interface IAuthService
    {
        bool ValidateCredentials(string username, string password);
    }

    public sealed class AuthService : IAuthService
    {
        private readonly IAccountRepository _accounts;

        public AuthService(IAccountRepository accounts) => _accounts = accounts;

        public bool ValidateCredentials(string username, string password)
        {
            var account = _accounts.GetByUsername(username);
            if (account is null)
                return false;

            // TODO: replace with a real password hash comparison.
            return account.PasswordHash == password;
        }
    }
}
