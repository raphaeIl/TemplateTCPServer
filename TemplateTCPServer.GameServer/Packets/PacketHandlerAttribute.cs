using TemplateTCPServer.Common.Protocol;

namespace TemplateTCPServer.GameServer.Packets
{
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
