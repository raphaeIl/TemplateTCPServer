using TemplateTCPServer.Common.Protocol;

namespace TemplateTCPServer.GameServer.Packets
{
    /// <summary>
    /// Marks a handler method as the handler for a given <see cref="MsgId"/>. Scanned once
    /// at startup by the <see cref="PacketHandlerRegistry"/> to build the routing table.
    /// The declaring type is registered in DI and resolved per-packet by the
    /// <see cref="PacketDispatcher"/>, so handler methods can rely on constructor injection.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class PacketHandlerAttribute : Attribute
    {
        public MsgId MsgId { get; }

        public PacketHandlerAttribute(MsgId msgId)
        {
            MsgId = msgId;
        }
    }
}
