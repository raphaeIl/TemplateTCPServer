using System.Collections.Concurrent;
using System.Reflection;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.Common.Networking;

namespace TemplateTCPServer.Common.Packets
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
                IMessage? request = ParseRequest(entry.RequestType, packet.Payload);
                object? result = entry.Method.Invoke(handler, new object?[] { request, connection });

                // If the handler maps to a reply MsgId, frame its returned message and send it.
                if (entry.ReplyMsgId != MsgId.None && result is IMessage reply)
                {
                    connection.Send(new RawPacket(entry.ReplyMsgId, reply.ToByteArray()));
                }
            }
            catch (TargetInvocationException ex) when (ex.InnerException is NotImplementedException notImplemented)
            {
                logger.LogWarning(notImplemented,
                    "Handler {Type}.{Method} was called for {MsgId} but is not implemented",
                    entry.Type.Name, entry.Method.Name, packet.MsgId);
            }
            catch (NotImplementedException ex)
            {
                logger.LogWarning(ex,
                    "Handler {Type}.{Method} was called for {MsgId} but is not implemented",
                    entry.Type.Name, entry.Method.Name, packet.MsgId);
            }
            catch (Exception ex)
            {
                var actual = (ex as TargetInvocationException)?.InnerException ?? ex;
                logger.LogError(actual, "Handler {Type}.{Method} failed for {MsgId}",
                    entry.Type.Name, entry.Method.Name, packet.MsgId);
            }
        }

        // Strongly-typed parse for call sites that know the message type at compile time.
        // Uses the generated static `Parser` (a MessageParser<T>) so there is no boxing or
        // reflection on the hot path.
        public static T ParseRequest<T>(ReadOnlyMemory<byte> payload) where T : IMessage<T>
            => MessageParserCache<T>.Parser.ParseFrom(payload.Span);

        // Resolves the generated `MessageParser` for a protobuf request type and parses the
        // payload. Returns null when the handler declares no request type. The reflection
        // path is only reached by the runtime dispatcher, which knows the type as `Type`,
        // not as a compile-time `T`; the registry guarantees `requestType` implements IMessage.
        private IMessage? ParseRequest(Type? requestType, ReadOnlyMemory<byte> payload)
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

            // MessageParser.ParseFrom returns IMessage; the registry has already verified the
            // type implements IMessage, so this is safe.
            return parser.ParseFrom(payload.Span);
        }

        // Holds the generated MessageParser<T> for a known compile-time message type, resolved
        // once per closed generic. Faster and stricter than the by-Type reflection cache.
        private static class MessageParserCache<T> where T : IMessage<T>
        {
            public static readonly MessageParser<T> Parser =
                (MessageParser<T>)typeof(T)
                    .GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)!
                    .GetValue(null)!;
        }
    }
}
