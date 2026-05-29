using TemplateTCPServer.Data.Entities;

namespace TemplateTCPServer.Data.Repositories
{
    /// <summary>
    /// Account-specific repository. Demonstrates extending the generic
    /// <see cref="IRepository{T}"/> with a domain query.
    /// </summary>
    public interface IAccountRepository : IRepository<Account>
    {
        Task<Account?> GetByUsernameAsync(string username, CancellationToken ct = default);
    }
}
