using Microsoft.Extensions.DependencyInjection;


namespace TemplateTCPServer.Table
{
    public static class TableExtensions
    {
        public static IServiceCollection AddTableService(
            this IServiceCollection services)
        {
            services.AddSingleton<ITableService, TableService>();
            return services;
        }
    }
}
