using TemplateTCPServer.Data.Entities;

namespace TemplateTCPServer.Data.Repositories
{
    public interface IAccountRepository : IRepository<Account>
    {
        Task<Account?> GetByUsernameAsync(string username, CancellationToken ct = default);
        Task<int> CountAsync(CancellationToken ct = default);
    }
}
