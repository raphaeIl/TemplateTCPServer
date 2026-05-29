using TemplateTCPServer.Data.Repositories;

namespace TemplateTCPServer.GameServer.Services
{
    public interface IExampleService
    {
        int CountAccounts();
    }

    public sealed class ExampleService : IExampleService
    {
        private readonly IAccountRepository _accounts;

        public ExampleService(IAccountRepository accounts) => _accounts = accounts;

        public int CountAccounts()
            => _accounts.Count();
    }
}
