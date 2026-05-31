using TemplateTCPServer.Common.Protocol;

namespace TemplateTCPServer.Common.Packets
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class PacketHandlerAttribute : Attribute
    {
        // MsgId of the inbound packet this method handles.
        public MsgId ReqMsgId { get; }

        // MsgId used to frame the method's return value as a reply packet.
        // None (the default) means the method returns nothing to send.
        public MsgId RespMsgId { get; }

        public PacketHandlerAttribute(MsgId reqMsgId, MsgId respMsgId = MsgId.None)
        {
            ReqMsgId = reqMsgId;
            RespMsgId = respMsgId;
        }
    }
}
