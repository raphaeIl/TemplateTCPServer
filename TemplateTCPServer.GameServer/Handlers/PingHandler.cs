using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.GameServer.Networking;
using TemplateTCPServer.GameServer.Packets;
using TemplateTCPServer.GameServer.Services;

namespace TemplateTCPServer.GameServer.Handlers
{
    // Example handler. Implement IPacketHandler, take dependencies in the constructor, and
    // tag a (Connection, BasePacket) method with [PacketHandler(MsgId)].
    public sealed class PingHandler : IPacketHandler
    {
        private readonly IExampleService _example;
        private readonly ILogger<PingHandler> _logger;

        public PingHandler(IExampleService example, ILogger<PingHandler> logger)
        {
            _example = example;
            _logger = logger;
        }

        [PacketHandler(MsgId.Ping)]
        public void HandlePing(Connection connection, BasePacket packet)
        {
            // Guarded so the sample still replies when no database is configured.
            try
            {
                int accounts = _example.CountAccounts();
                _logger.LogInformation("Ping from {Id} (accounts in db: {Count})", connection.Id, accounts);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ping from {Id} (db unavailable, replying anyway)", connection.Id);
            }

            connection.Send(new RawPacket(MsgId.Pong, ReadOnlyMemory<byte>.Empty));
        }
    }
}
