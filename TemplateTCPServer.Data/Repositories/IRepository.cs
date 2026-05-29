namespace TemplateTCPServer.Data.Repositories
{
    /// <summary>
    /// Minimal generic repository contract. Concrete repositories extend this with
    /// entity-specific queries. Kept deliberately small for the template.
    /// </summary>
    public interface IRepository<T> where T : class
    {
        Task<T?> GetByIdAsync(long id, CancellationToken ct = default);
        Task AddAsync(T entity, CancellationToken ct = default);
        void Remove(T entity);

        /// <summary>Persists pending changes. (A UnitOfWork can own this instead later.)</summary>
        Task<int> SaveChangesAsync(CancellationToken ct = default);
    }
}
