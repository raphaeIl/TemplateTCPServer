namespace TemplateTCPServer.Common.Protocol
{
    public interface IPacketSerializer
    {
        BasePacket Deserialize(MsgId msgId, ReadOnlyMemory<byte> payload);

        ReadOnlyMemory<byte> Serialize(BasePacket packet);
    }
}
