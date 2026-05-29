using TemplateTCPServer.Data.Repositories;

namespace TemplateTCPServer.GameServer.Services
{
    public interface IExampleService
    {
        Task<int> CountAccountsAsync(CancellationToken ct = default);
    }

    public sealed class ExampleService : IExampleService
    {
        private readonly IAccountRepository _accounts;

        public ExampleService(IAccountRepository accounts) => _accounts = accounts;

        public Task<int> CountAccountsAsync(CancellationToken ct = default)
            => _accounts.CountAsync(ct);
    }
}
