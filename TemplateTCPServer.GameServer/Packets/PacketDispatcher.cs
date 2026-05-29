using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.GameServer.Networking;

namespace TemplateTCPServer.GameServer.Packets
{
    public sealed class PacketDispatcher(
        IServiceProvider rootProvider,
        PacketHandlerRegistry registry,
        ILogger<PacketDispatcher> logger)
    {
        public void Dispatch(Connection connection, BasePacket packet)
        {
            if (!registry.TryGet(packet.MsgId, out var entry))
            {
                logger.LogWarning("No handler for {MsgId}; packet dropped", packet.MsgId);
                return;
            }

            // One DI scope per packet; the handler and its scoped deps live for this packet only.
            using var scope = rootProvider.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService(entry.Type);

            try
            {
                entry.Method.Invoke(handler, new object[] { connection, packet });
            }
            catch (Exception ex)
            {
                var actual = (ex as System.Reflection.TargetInvocationException)?.InnerException ?? ex;
                logger.LogError(actual, "Handler {Type}.{Method} failed for {MsgId}",
                    entry.Type.Name, entry.Method.Name, packet.MsgId);
            }
        }
    }
}
