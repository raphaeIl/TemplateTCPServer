using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.GameServer.Networking;

namespace TemplateTCPServer.GameServer.Packets
{
    /// <summary>
    /// Routes an incoming packet to its handler. This is the TCP-side equivalent of MVC's
    /// controller-activation middleware: there is no framework opening a scope for a socket
    /// message, so the dispatcher does it. It is a singleton (stateless), but for <b>each
    /// packet</b> it opens a fresh DI scope and resolves the handler from that scope &mdash;
    /// so the handler and everything it injects (services, repositories, the scoped
    /// <c>AppDbContext</c>) live for exactly one packet, mirroring an HTTP request scope.
    ///
    /// Replaces the invoke half of the old <c>PacketHandlerFactory</c> (which used
    /// <c>Activator.CreateInstance</c> and therefore could not do constructor injection).
    /// </summary>
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

            // One DI scope per packet == the "request scope".
            await using var scope = _rootProvider.CreateAsyncScope();

            // The handler instance comes from the scope -> constructor injection works,
            // and any scoped deps it pulls (DbContext, repos) are disposed with the scope.
            var handler = scope.ServiceProvider.GetRequiredService(entry.Type);

            try
            {
                object? result = entry.Method.Invoke(handler, new object[] { connection, packet });
                if (result is Task task)
                    await task; // support async handler methods
            }
            catch (Exception ex)
            {
                // Unwrap the reflection wrapper so logs show the real handler exception.
                var actual = (ex as System.Reflection.TargetInvocationException)?.InnerException ?? ex;
                _logger.LogError(actual, "Handler {Type}.{Method} failed for {MsgId}",
                    entry.Type.Name, entry.Method.Name, packet.MsgId);
            }
        }
    }
}
