using TemplateTCPServer.Data.Repositories;

namespace TemplateTCPServer.SDKServer.Services
{
    /// <summary>
    /// Scoped service: depends on the scoped <see cref="IAccountRepository"/> which depends
    /// on the scoped <c>AppDbContext</c>. Demonstrates the Service -&gt; Repository -&gt;
    /// DbContext layering. Password handling is intentionally left as a stub.
    /// </summary>
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
