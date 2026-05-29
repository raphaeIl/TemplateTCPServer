using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TemplateTCPServer.Data.Repositories;

namespace TemplateTCPServer.Data
{
    public static class DependencyInjection
    {
        /// <summary>
        /// Registers the EF Core context (Postgres) and repositories. Both the GameServer
        /// (per-packet scope) and the SDKServer (per-request scope) resolve these scoped
        /// services from their respective scopes.
        /// </summary>
        public static IServiceCollection AddDataLayer(
            this IServiceCollection services, IConfiguration config)
        {
            services.AddDbContext<AppDbContext>(opt =>
                opt.UseNpgsql(
                    config.GetConnectionString("Postgres"),
                    npg => npg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

            services.AddScoped<IAccountRepository, AccountRepository>();

            return services;
        }
    }
}
