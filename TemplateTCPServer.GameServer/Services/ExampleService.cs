using TemplateTCPServer.Data.Repositories;

namespace TemplateTCPServer.GameServer.Services
{
    public interface IExampleService
    {
        int CountAccounts();
    }

    public sealed class ExampleService(IAccountRepository accounts) : IExampleService
    {
        public int CountAccounts()
            => accounts.Count();
    }
}
