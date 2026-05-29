using Microsoft.EntityFrameworkCore;
using TemplateTCPServer.Data.Entities;

namespace TemplateTCPServer.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Account> Accounts => Set<Account>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Account>(e =>
            {
                e.HasKey(a => a.Id);
                e.HasIndex(a => a.Username).IsUnique();
                e.Property(a => a.Username).IsRequired();
            });
        }
    }
}
