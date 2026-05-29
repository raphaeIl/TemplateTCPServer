namespace TemplateTCPServer.GameServer.Packets
{
    // Marker for handler classes. Handler methods are tagged with [PacketHandler(MsgId)]
    // and have the signature (Connection connection, BasePacket packet) returning void or Task.
    public interface IPacketHandler
    {
    }
}
