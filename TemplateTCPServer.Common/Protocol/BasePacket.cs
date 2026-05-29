namespace TemplateTCPServer.Common.Protocol
{
    /// <summary>
    /// Base envelope for every message that crosses the wire. A packet carries its
    /// <see cref="MsgId"/> (used for routing to a handler) and an opaque payload.
    /// Concrete packet types derive from this; the serializer turns the payload bytes
    /// into the typed body via <see cref="As{T}"/>.
    /// </summary>
    public abstract class BasePacket
    {
        /// <summary>Routing id for this message.</summary>
        public abstract MsgId MsgId { get; }

        /// <summary>The raw payload bytes (everything after the framing header).</summary>
        public ReadOnlyMemory<byte> Payload { get; init; }

        protected BasePacket() { }

        protected BasePacket(ReadOnlyMemory<byte> payload)
        {
            Payload = payload;
        }
    }
}
