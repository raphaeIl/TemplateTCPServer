namespace TemplateTCPServer.Data.Core
{
    public class Repository<T>(AppDbContext db) : IRepository<T> where T : class
    {
        protected readonly AppDbContext Db = db;

        public virtual T? GetById(long id)
            => Db.Set<T>().Find(id);

        public virtual void Add(T entity)
            => Db.Set<T>().Add(entity);

        public virtual void Remove(T entity)
            => Db.Set<T>().Remove(entity);

        public virtual int SaveChanges()
            => Db.SaveChanges();
    }
}
