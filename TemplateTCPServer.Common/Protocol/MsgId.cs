namespace TemplateTCPServer.Common.Protocol
{
    public enum MsgId : ushort
    {
        None = 0,

        Ping = 1,
        Pong = 2,
        MsgPingRequest = 3,
    }
}
