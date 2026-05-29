using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.GameServer.Hosting;
using TemplateTCPServer.GameServer.Networking;
using TemplateTCPServer.GameServer.Packets;

namespace TemplateTCPServer.GameServer
{
    public static class GameServerExtensions
    {
        public static IServiceCollection AddGameServer(this IServiceCollection services)
        {
            // Local copy is only used to enumerate handler types for registration below;
            // the resolvable singleton is registered via factory so it gets the host logger.
            var registry = new PacketHandlerRegistry(new[] { typeof(GameServerExtensions).Assembly });

            services.AddSingleton(sp => new PacketHandlerRegistry(
                new[] { typeof(GameServerExtensions).Assembly },
                sp.GetService<ILogger<PacketHandlerRegistry>>()));

            // Scoped so each handler is resolved inside the dispatcher's per-packet scope.
            foreach (var handlerType in registry.HandlerTypes)
                services.AddScoped(handlerType);

            services.AddScoped<Services.IExampleService, Services.ExampleService>();

            services.AddSingleton<IPacketSerializer, PassthroughPacketSerializer>();
            services.AddSingleton<PacketDispatcher>();
            services.AddSingleton<ConnectionManager>();

            services.AddHostedService<GameServerHostedService>();

            return services;
        }
    }
}
