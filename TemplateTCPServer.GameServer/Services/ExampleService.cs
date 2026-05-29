using TemplateTCPServer.Data.Repositories;

namespace TemplateTCPServer.GameServer.Services
{
    public sealed class ExampleService : IExampleService
    {
        private readonly IAccountRepository _accounts;

        // Repository injected by DI -> resolved from the per-packet scope -> wraps the
        // scoped AppDbContext. This is the Service -> Repository -> DbContext layering.
        public ExampleService(IAccountRepository accounts) => _accounts = accounts;

        public Task<int> CountAccountsAsync(CancellationToken ct = default)
            => _accounts.CountAsync(ct);
    }
}
