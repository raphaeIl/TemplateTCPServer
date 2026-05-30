using System.Collections.Concurrent;
using System.Reflection;
using Google.Protobuf;
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
        // Cache of request-type -> MessageParser (the generated static `Parser` property),
        // so we resolve the protobuf parser by reflection only once per type.
        private readonly ConcurrentDictionary<Type, MessageParser> _parsers = new();

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
                // Parse the payload into the handler's protobuf request type, then invoke
                // the generated signature: (TRequest request, Connection connection).
                var request = ParseRequest(entry.RequestType, packet.Payload);
                var result = entry.Method.Invoke(handler, new object?[] { request, connection });

                // If the handler maps to a reply MsgId, frame its returned message and send it.
                if (entry.ReplyMsgId != MsgId.None && result is IMessage reply)
                {
                    connection.Send(new RawPacket(entry.ReplyMsgId, reply.ToByteArray()));
                }
            }
            catch (Exception ex)
            {
                var actual = (ex as TargetInvocationException)?.InnerException ?? ex;
                logger.LogError(actual, "Handler {Type}.{Method} failed for {MsgId}",
                    entry.Type.Name, entry.Method.Name, packet.MsgId);
            }
        }

        // Resolves the generated `MessageParser` for a protobuf request type and parses the
        // payload. Returns null when the handler declares no request type.
        private object? ParseRequest(Type? requestType, ReadOnlyMemory<byte> payload)
        {
            if (requestType is null)
                return null;

            var parser = _parsers.GetOrAdd(requestType, static t =>
            {
                var prop = t.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)
                    ?? throw new InvalidOperationException(
                        $"Request type {t.Name} has no static Parser; is it a protobuf message?");
                return (MessageParser)prop.GetValue(null)!;
            });

            return parser.ParseFrom(payload.Span);
        }
    }
}
