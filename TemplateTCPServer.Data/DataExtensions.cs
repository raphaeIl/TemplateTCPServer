using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TemplateTCPServer.Data.Core;
using TemplateTCPServer.Data.Repositories;

namespace TemplateTCPServer.Data
{
    public static class DataExtensions
    {
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
