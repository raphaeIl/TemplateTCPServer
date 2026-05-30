using System.Reflection;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;

namespace TemplateTCPServer.Common.Packets
{
    public sealed class PacketHandlerRegistry
    {
        private readonly Dictionary<MsgId, HandlerEntry> _map = new();

        public PacketHandlerRegistry(IEnumerable<Assembly> handlerAssemblies, ILogger<PacketHandlerRegistry>? logger = null)
        {
            var handlerTypes = handlerAssemblies
                .SelectMany(a => a.GetTypes())
                .Where(t => typeof(IPacketHandler).IsAssignableFrom(t)
                            && t is { IsInterface: false, IsAbstract: false });

            foreach (var type in handlerTypes)
            {
                foreach (var method in type.GetMethods())
                {
                    // Look on the method itself, then on its base definition. The latter lets a
                    // handler declare its RPC surface once on virtual methods of an abstract base
                    // (the "proto" base) and have concrete overrides inherit the [PacketHandler]
                    // mapping without re-declaring the attribute. `method` stays the most-derived
                    // MethodInfo, so Invoke dispatches to the override.
                    var attr = method.GetCustomAttribute<PacketHandlerAttribute>(inherit: true)
                               ?? method.GetBaseDefinition().GetCustomAttribute<PacketHandlerAttribute>(inherit: false);
                    if (attr is null)
                        continue;

                    // The generated handler signature is (TRequest request, Connection connection).
                    // Capture the request parameter type so the dispatcher can parse the
                    // packet payload into it; null if the method takes no typed request.
                    var requestType = method.GetParameters().Length > 0
                        ? method.GetParameters()[0].ParameterType
                        : null;

                    // The dispatcher parses the payload with the protobuf parser, so the request
                    // type must be a protobuf message. Reject anything else at startup rather than
                    // failing per-packet at runtime with an opaque "no static Parser" error.
                    if (requestType is not null && !typeof(IMessage).IsAssignableFrom(requestType))
                    {
                        throw new InvalidOperationException(
                            $"{type.Name}.{method.Name} maps {attr.MsgId} but its request parameter " +
                            $"'{requestType.Name}' does not implement {nameof(IMessage)}; " +
                            "packet handlers must take a protobuf message as their first parameter.");
                    }

                    var entry = new HandlerEntry(type, method, requestType, attr.ReplyMsgId);
                    if (!_map.TryAdd(attr.MsgId, entry))
                    {
                        logger?.LogWarning("Duplicate handler for {MsgId}; {Type}.{Method} ignored",
                            attr.MsgId, type.Name, method.Name);
                        continue;
                    }

                    logger?.LogInformation("Mapped {MsgId} -> {Type}.{Method} (reply: {Reply})",
                        attr.MsgId, type.Name, method.Name, attr.ReplyMsgId);
                }
            }
        }

        public bool TryGet(MsgId msgId, out HandlerEntry entry) => _map.TryGetValue(msgId, out entry);

        public IEnumerable<Type> HandlerTypes => _map.Values.Select(e => e.Type).Distinct();
    }

    public readonly record struct HandlerEntry(Type Type, MethodInfo Method, Type? RequestType, MsgId ReplyMsgId);
}
