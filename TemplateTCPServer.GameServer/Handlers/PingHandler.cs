using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.GameServer.Networking;
using TemplateTCPServer.GameServer.Packets;
using TemplateTCPServer.GameServer.Services;

namespace TemplateTCPServer.GameServer.Handlers
{
    /// <summary>
    /// Example packet handler &mdash; the TCP-side analog of an MVC controller.
    ///
    /// It is discovered by the <see cref="PacketHandlerRegistry"/> (via the
    /// <see cref="PacketHandlerAttribute"/>), registered in DI as Scoped, and resolved by
    /// the <see cref="PacketDispatcher"/> inside a fresh per-packet scope. Because of that,
    /// its constructor dependencies are injected just like a controller's:
    /// <see cref="IExampleService"/> (scoped) -&gt; <c>IAccountRepository</c> (scoped) -&gt;
    /// <c>AppDbContext</c> (scoped), all living for exactly this one packet.
    ///
    /// To add your own handler: implement <see cref="IPacketHandler"/>, take whatever
    /// services you need in the constructor, and put <c>[PacketHandler(MsgId.X)]</c> on a
    /// method with the signature <c>(Connection, BasePacket)</c> returning void or Task.
    /// </summary>
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
        public async Task HandlePing(Connection connection, BasePacket packet)
        {
            // Exercise the Service -> Repository -> DbContext chain. Guarded so the sample
            // still responds even if no database is configured/reachable.
            try
            {
                int accounts = await _example.CountAccountsAsync();
                _logger.LogInformation("Ping from {Id} (accounts in db: {Count})", connection.Id, accounts);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ping from {Id} (db unavailable, replying anyway)", connection.Id);
            }

            await connection.SendAsync(new RawPacket(MsgId.Pong, ReadOnlyMemory<byte>.Empty));
        }
    }
}
