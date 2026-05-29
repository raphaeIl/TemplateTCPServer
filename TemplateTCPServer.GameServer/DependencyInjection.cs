using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.GameServer.Hosting;
using TemplateTCPServer.GameServer.Networking;
using TemplateTCPServer.GameServer.Packets;

namespace TemplateTCPServer.GameServer
{
    public static class DependencyInjection
    {
        /// <summary>
        /// Registers the TCP game server: the framing serializer, the handler routing table,
        /// every discovered packet handler (Scoped, so handlers get constructor injection),
        /// the dispatcher and connection manager (singletons), and the hosted listener.
        /// </summary>
        public static IServiceCollection AddGameServer(this IServiceCollection services)
        {
            // Build the routing table once, here, so we can both register it as a singleton
            // and enumerate the handler types to register them in DI. (Discovery logging is
            // done by the host-resolved registry registered below; this local copy is only
            // used to enumerate handler types.)
            var registry = new PacketHandlerRegistry(new[] { typeof(DependencyInjection).Assembly });

            // Register the registry via a factory so it gets the host's real logger.
            services.AddSingleton(sp => new PacketHandlerRegistry(
                new[] { typeof(DependencyInjection).Assembly },
                sp.GetService<ILogger<PacketHandlerRegistry>>()));

            // Each handler type is Scoped: resolved fresh inside the per-packet scope the
            // dispatcher opens, so it (and its injected services/repositories/DbContext) are
            // scoped to a single packet.
            foreach (var handlerType in registry.HandlerTypes)
                services.AddScoped(handlerType);

            // Framing serializer (swap PassthroughPacketSerializer for a real one later).
            services.AddSingleton<IPacketSerializer, PassthroughPacketSerializer>();

            services.AddSingleton<PacketDispatcher>();
            services.AddSingleton<ConnectionManager>();

            services.AddHostedService<GameServerHostedService>();

            return services;
        }
    }
}
