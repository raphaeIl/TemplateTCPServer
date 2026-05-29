namespace TemplateTCPServer.Common.Protocol
{
    public abstract class BasePacket
    {
        public abstract MsgId MsgId { get; }

        public ReadOnlyMemory<byte> Payload { get; init; }

        protected BasePacket() { }

        protected BasePacket(ReadOnlyMemory<byte> payload)
        {
            Payload = payload;
        }
    }
}
