namespace TemplateTCPServer.Common.Protocol
{
    /// <summary>
    /// Default serializer for the bare-bones template: it does no body encoding and just
    /// wraps the raw payload in a <see cref="RawPacket"/>. Replace with a JSON or binary
    /// serializer (and typed packet classes) when the protocol is fleshed out.
    /// </summary>
    public sealed class PassthroughPacketSerializer : IPacketSerializer
    {
        public BasePacket Deserialize(MsgId msgId, ReadOnlyMemory<byte> payload)
            => new RawPacket(msgId, payload);

        public ReadOnlyMemory<byte> Serialize(BasePacket packet)
            => packet.Payload;
    }
}
