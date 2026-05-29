using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.GameServer.Networking;
using TemplateTCPServer.GameServer.Packets;
using TemplateTCPServer.GameServer.Services;

namespace TemplateTCPServer.GameServer.Handlers
{
    // Example handler. Implement IPacketHandler, take dependencies as primary-constructor
    // parameters, and tag a (Connection, BasePacket) method with [PacketHandler(MsgId)].
    public sealed class PingHandler(
        IExampleService example,
        ILogger<PingHandler> logger) : IPacketHandler
    {
        [PacketHandler(MsgId.Ping)]
        public void HandlePing(Connection connection, BasePacket packet)
        {
            // Guarded so the sample still replies when no database is configured.
            try
            {
                int accounts = example.CountAccounts();
                logger.LogInformation("Ping from {Id} (accounts in db: {Count})", connection.Id, accounts);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ping from {Id} (db unavailable, replying anyway)", connection.Id);
            }

            connection.Send(new RawPacket(MsgId.Pong, ReadOnlyMemory<byte>.Empty));
        }
    }
}
