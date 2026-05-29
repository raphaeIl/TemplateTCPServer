using Microsoft.EntityFrameworkCore;
using TemplateTCPServer.Data.Entities;

namespace TemplateTCPServer.Data
{
    /// <summary>
    /// The single EF Core context for the application. Registered as Scoped by
    /// <c>AddDbContext</c>, so one instance lives per DI scope &mdash; per HTTP request on
    /// the SDK side, and per packet on the GameServer side (see the PacketDispatcher).
    /// </summary>
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
