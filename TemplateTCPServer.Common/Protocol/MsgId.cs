namespace TemplateTCPServer.Common.Protocol
{
    public enum MsgId : ushort
    {
        None = 0,

        // PingService RPCs (see Protocol/ping.proto). One MsgId per rpc method.
        Ping = 1,
        Pong = 2,
    }
}
