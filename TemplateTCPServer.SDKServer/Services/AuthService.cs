using TemplateTCPServer.Data.Repositories;

namespace TemplateTCPServer.SDKServer.Services
{
    public interface IAuthService
    {
        Task<bool> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default);
    }

    public sealed class AuthService : IAuthService
    {
        private readonly IAccountRepository _accounts;

        public AuthService(IAccountRepository accounts) => _accounts = accounts;

        public async Task<bool> ValidateCredentialsAsync(string username, string password, CancellationToken ct = default)
        {
            var account = await _accounts.GetByUsernameAsync(username, ct);
            if (account is null)
                return false;

            // TODO: replace with a real password hash comparison.
            return account.PasswordHash == password;
        }
    }
}
