namespace TemplateTCPServer.Common.Protocol
{
    /// <summary>
    /// Minimal concrete <see cref="BasePacket"/> the framing layer produces when it has
    /// read a complete frame off the wire but has no typed packet class for the
    /// <see cref="MsgId"/>. Handlers can read <see cref="BasePacket.Payload"/> directly,
    /// or a richer protocol can replace this with strongly-typed packet classes later.
    /// </summary>
    public sealed class RawPacket : BasePacket
    {
        public override MsgId MsgId { get; }

        public RawPacket(MsgId msgId, ReadOnlyMemory<byte> payload) : base(payload)
        {
            MsgId = msgId;
        }
    }
}
