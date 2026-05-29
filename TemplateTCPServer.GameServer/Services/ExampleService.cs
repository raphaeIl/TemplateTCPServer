using TemplateTCPServer.Data.Repositories;

namespace TemplateTCPServer.GameServer.Services
{
    public sealed class ExampleService(IAccountRepository accounts) : IExampleService
    {
        public int CountAccounts()
            => accounts.Count();
    }

    public interface IExampleService
    {
        int CountAccounts();
    }
}
