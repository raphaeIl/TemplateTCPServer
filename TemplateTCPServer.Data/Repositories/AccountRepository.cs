using System.Linq;
using TemplateTCPServer.Data.Core;
using TemplateTCPServer.Data.Entities;

namespace TemplateTCPServer.Data.Repositories
{
    public sealed class AccountRepository(AppDbContext db) : Repository<Account>(db), IAccountRepository
    {
        public Account? GetByUsername(string username)
            => Db.Accounts.SingleOrDefault(a => a.Username == username);

        public int Count()
            => Db.Accounts.Count();
    }

    public interface IAccountRepository : IRepository<Account>
    {
        Account? GetByUsername(string username);
        int Count();
    }
}
