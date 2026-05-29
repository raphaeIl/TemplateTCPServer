namespace TemplateTCPServer.Common.Protocol
{
    /// <summary>
    /// Turns wire bytes into <see cref="BasePacket"/> instances and back. The framing
    /// (length prefix + msg id) is handled by the transport (see the GameServer's
    /// connection read loop); this type is only responsible for the body of a single
    /// already-framed message.
    /// </summary>
    public interface IPacketSerializer
    {
        /// <summary>
        /// Builds a packet from a decoded frame. <paramref name="payload"/> is the body
        /// bytes (everything after the framing header).
        /// </summary>
        BasePacket Deserialize(MsgId msgId, ReadOnlyMemory<byte> payload);

        /// <summary>Serializes a packet's body to bytes (the payload, not the frame).</summary>
        ReadOnlyMemory<byte> Serialize(BasePacket packet);
    }
}
