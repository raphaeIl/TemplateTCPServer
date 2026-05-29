namespace TemplateTCPServer.GameServer.Packets
{
    /// <summary>
    /// Marker interface for packet handler classes. A handler is the TCP-side analog of an
    /// MVC controller: it is discovered by <see cref="PacketHandlerRegistry"/>, registered
    /// in DI as Scoped, and its <c>[PacketHandler(MsgId)]</c> methods are invoked per packet.
    ///
    /// Handler method signature is: <c>(Connection connection, BasePacket packet)</c>,
    /// returning <c>void</c> or <c>Task</c>.
    /// </summary>
    public interface IPacketHandler
    {
    }
}
