using System.Reflection;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;

namespace TemplateTCPServer.GameServer.Packets
{
    /// <summary>
    /// Routing table built once at startup. Reflection is used <b>only</b> to discover the
    /// <see cref="MsgId"/> &rarr; (handler type, method) mapping &mdash; no handler instances
    /// are created here. The <see cref="PacketDispatcher"/> resolves the instance per packet
    /// from DI. This replaces the discovery half of the old <c>PacketHandlerFactory</c>.
    /// </summary>
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
                    var attr = method.GetCustomAttribute<PacketHandlerAttribute>(inherit: false);
                    if (attr is null)
                        continue;

                    if (!_map.TryAdd(attr.MsgId, new HandlerEntry(type, method)))
                    {
                        logger?.LogWarning("Duplicate handler for {MsgId}; {Type}.{Method} ignored",
                            attr.MsgId, type.Name, method.Name);
                        continue;
                    }

                    logger?.LogInformation("Mapped {MsgId} -> {Type}.{Method}",
                        attr.MsgId, type.Name, method.Name);
                }
            }
        }

        /// <summary>Looks up the handler entry for a message id.</summary>
        public bool TryGet(MsgId msgId, out HandlerEntry entry) => _map.TryGetValue(msgId, out entry);

        /// <summary>The distinct handler CLR types, so they can be registered in DI.</summary>
        public IEnumerable<Type> HandlerTypes => _map.Values.Select(e => e.Type).Distinct();
    }

    /// <summary>A discovered handler: the declaring type and the attributed method on it.</summary>
    public readonly record struct HandlerEntry(Type Type, MethodInfo Method);
}
