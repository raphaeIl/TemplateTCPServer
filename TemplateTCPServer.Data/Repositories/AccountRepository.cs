using Microsoft.EntityFrameworkCore;
using TemplateTCPServer.Data.Core;
using TemplateTCPServer.Data.Entities;

namespace TemplateTCPServer.Data.Repositories
{
    public interface IAccountRepository : IRepository<Account>
    {
        Task<Account?> GetByUsernameAsync(string username, CancellationToken ct = default);
        Task<int> CountAsync(CancellationToken ct = default);
    }

    public sealed class AccountRepository : Repository<Account>, IAccountRepository
    {
        public AccountRepository(AppDbContext db) : base(db) { }

        public Task<Account?> GetByUsernameAsync(string username, CancellationToken ct = default)
            => Db.Accounts.SingleOrDefaultAsync(a => a.Username == username, ct);

        public Task<int> CountAsync(CancellationToken ct = default)
            => Db.Accounts.CountAsync(ct);
    }
}
