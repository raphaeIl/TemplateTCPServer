using TemplateTCPServer.Common.Protocol;

namespace TemplateTCPServer.Common.Packets
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class PacketHandlerAttribute : Attribute
    {
        // MsgId of the inbound packet this method handles.
        public MsgId MsgId { get; }

        // MsgId used to frame the method's return value as a reply packet.
        // None (the default) means the method returns nothing to send.
        public MsgId ReplyMsgId { get; }

        public PacketHandlerAttribute(MsgId msgId, MsgId replyMsgId = MsgId.None)
        {
            MsgId = msgId;
            ReplyMsgId = replyMsgId;
        }
    }
}
