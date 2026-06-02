using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Networking;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.GameServer.Services;

namespace TemplateTCPServer.GameServer.Handlers
{
    // Implementation of the PingService "proto". Derives from the generated
    // PingHandlerBase and overrides the RPC methods with
    // typed protobuf request/response. The [PacketHandler(MsgId.X, MsgId.Y)]
    // mapping is inherited from the base — no need to re-declare it here. The
    // dispatcher parses the payload into the request, invokes the override, and
    // frames the returned message as the reply packet.
    public sealed class PingHandler(
        IExampleService example,
        ILogger<PingHandler> logger) : PingHandlerBase
    {
        public override void Ping(PingRequest request, Connection connection)
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
        }

    }
}
