using Microsoft.Extensions.DependencyInjection;
using TemplateTCPServer.SDKServer.Controllers;
using TemplateTCPServer.SDKServer.Services;

namespace TemplateTCPServer.SDKServer
{
    public static class SdkServerExtensions
    {
        public static IServiceCollection AddSdkServer(this IServiceCollection services)
        {
            services.AddScoped<IAuthService, AuthService>();

            services.AddControllers()
                    .AddApplicationPart(typeof(SDKController).Assembly);

            return services;
        }
    }
}
