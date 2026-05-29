using TemplateTCPServer.Data.Repositories;

namespace TemplateTCPServer.SDKServer.Services
{
    public sealed class AuthService(IAccountRepository accounts) : IAuthService
    {
        public bool ValidateCredentials(string username, string password)
        {
            var account = accounts.GetByUsername(username);
            if (account is null)
                return false;

            // TODO: replace with a real password hash comparison.
            return account.PasswordHash == password;
        }
    }

    public interface IAuthService
    {
        bool ValidateCredentials(string username, string password);
    }
}
