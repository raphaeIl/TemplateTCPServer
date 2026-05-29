using System.Reflection;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;

namespace TemplateTCPServer.GameServer.Packets
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

        public bool TryGet(MsgId msgId, out HandlerEntry entry) => _map.TryGetValue(msgId, out entry);

        public IEnumerable<Type> HandlerTypes => _map.Values.Select(e => e.Type).Distinct();
    }

    public readonly record struct HandlerEntry(Type Type, MethodInfo Method);
}
