using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace TemplateTCPServer.Table
{
    public static class TableExtensions
    {
        public static IServiceCollection AddTableService(
            this IServiceCollection services, IConfiguration config)
        {
            // Allow overriding the default resource directory via config.
            var resourceDir = config.GetValue<string>("Table:ResourceDir");
            if (!string.IsNullOrEmpty(resourceDir))
                TableService.ResourceDir = Path.GetFullPath(resourceDir);

            services.AddSingleton<ITableService, TableService>();
            return services;
        }
    }
}
