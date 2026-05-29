using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.GameServer.Networking;

namespace TemplateTCPServer.GameServer.Packets
{
    public sealed class PacketDispatcher
    {
        private readonly IServiceProvider _rootProvider;
        private readonly PacketHandlerRegistry _registry;
        private readonly ILogger<PacketDispatcher> _logger;

        public PacketDispatcher(
            IServiceProvider rootProvider,
            PacketHandlerRegistry registry,
            ILogger<PacketDispatcher> logger)
        {
            _rootProvider = rootProvider;
            _registry = registry;
            _logger = logger;
        }

        public void Dispatch(Connection connection, BasePacket packet)
        {
            if (!_registry.TryGet(packet.MsgId, out var entry))
            {
                _logger.LogWarning("No handler for {MsgId}; packet dropped", packet.MsgId);
                return;
            }

            // One DI scope per packet; the handler and its scoped deps live for this packet only.
            using var scope = _rootProvider.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService(entry.Type);

            try
            {
                entry.Method.Invoke(handler, new object[] { connection, packet });
            }
            catch (Exception ex)
            {
                var actual = (ex as System.Reflection.TargetInvocationException)?.InnerException ?? ex;
                _logger.LogError(actual, "Handler {Type}.{Method} failed for {MsgId}",
                    entry.Type.Name, entry.Method.Name, packet.MsgId);
            }
        }
    }
}
