namespace TemplateTCPServer.Common.Protocol
{
    public sealed class PassthroughPacketSerializer : IPacketSerializer
    {
        public BasePacket Deserialize(MsgId msgId, ReadOnlyMemory<byte> payload)
            => new RawPacket(msgId, payload);

        public ReadOnlyMemory<byte> Serialize(BasePacket packet)
            => packet.Payload;
    }
}
