namespace TemplateTCPServer.Common.Protocol
{
    public sealed class RawPacket : BasePacket
    {
        public override MsgId MsgId { get; }

        public RawPacket(MsgId msgId, ReadOnlyMemory<byte> payload) : base(payload)
        {
            MsgId = msgId;
        }
    }
}
