namespace TemplateTCPServer.Data.Core
{
    public interface IRepository<T> where T : class
    {
        T? GetById(long id);
        void Add(T entity);
        void Remove(T entity);
        int SaveChanges();
    }
}
