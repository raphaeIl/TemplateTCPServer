using Microsoft.Extensions.DependencyInjection;
using TemplateTCPServer.SDKServer.Controllers;
using TemplateTCPServer.SDKServer.Services;

namespace TemplateTCPServer.SDKServer
{
    public static class DependencyInjection
    {
        /// <summary>
        /// Registers the SDK (login/HTTP) side onto the shared host: its controllers (pulled
        /// in as an application part, since they live in this referenced library) and its
        /// scoped services. There is no <c>Main</c> here &mdash; the startup project owns the
        /// single web host.
        /// </summary>
        public static IServiceCollection AddSdkServer(this IServiceCollection services)
        {
            services.AddScoped<IAuthService, AuthService>();

            services.AddControllers()
                    .AddApplicationPart(typeof(SDKController).Assembly);

            return services;
        }
    }
}
