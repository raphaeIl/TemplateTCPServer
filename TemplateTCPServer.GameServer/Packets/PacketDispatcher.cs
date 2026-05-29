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

        public async Task DispatchAsync(Connection connection, BasePacket packet, CancellationToken ct)
        {
            if (!_registry.TryGet(packet.MsgId, out var entry))
            {
                _logger.LogWarning("No handler for {MsgId}; packet dropped", packet.MsgId);
                return;
            }

            // One DI scope per packet; the handler and its scoped deps live for this packet only.
            await using var scope = _rootProvider.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService(entry.Type);

            try
            {
                object? result = entry.Method.Invoke(handler, new object[] { connection, packet });
                if (result is Task task)
                    await task;
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
