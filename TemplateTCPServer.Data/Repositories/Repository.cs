using Microsoft.EntityFrameworkCore;

namespace TemplateTCPServer.Data.Repositories
{
    public class Repository<T> : IRepository<T> where T : class
    {
        protected readonly AppDbContext Db;

        public Repository(AppDbContext db) => Db = db;

        public virtual Task<T?> GetByIdAsync(long id, CancellationToken ct = default)
            => Db.Set<T>().FindAsync(new object?[] { id }, ct).AsTask();

        public virtual async Task AddAsync(T entity, CancellationToken ct = default)
            => await Db.Set<T>().AddAsync(entity, ct);

        public virtual void Remove(T entity)
            => Db.Set<T>().Remove(entity);

        public virtual Task<int> SaveChangesAsync(CancellationToken ct = default)
            => Db.SaveChangesAsync(ct);
    }
}
