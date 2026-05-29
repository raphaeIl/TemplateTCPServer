namespace TemplateTCPServer.Common.Protocol
{
    /// <summary>
    /// Wire identifier for a packet. Each value maps to exactly one packet handler
    /// (see <c>[PacketHandler(MsgId)]</c> in the GameServer). Concrete message ids
    /// are intentionally left out of this bare-bones template &mdash; add them here
    /// as the protocol grows.
    /// </summary>
    public enum MsgId : ushort
    {
        None = 0,

        // Example ids used by the sample handler. Real protocols replace/extend these.
        Ping = 1,
        Pong = 2,
    }
}
