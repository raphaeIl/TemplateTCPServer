namespace TemplateTCPServer.GameServer.Services
{
    public interface IExampleService
    {
        Task<int> CountAccountsAsync(CancellationToken ct = default);
    }
}
