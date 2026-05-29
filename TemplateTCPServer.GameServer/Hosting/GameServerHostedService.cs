using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.GameServer.Networking;
using TemplateTCPServer.GameServer.Packets;

namespace TemplateTCPServer.GameServer.Hosting
{
    /// <summary>
    /// The main TCP server, run as a hosted background service inside the shared generic
    /// host. Owns the <see cref="TcpListener"/>, accepts clients, and hands each to its own
    /// <see cref="Connection"/> read loop. Replaces the old singleton <c>GameServer</c> +
    /// <c>Task.Run</c>; the host now owns its lifecycle and shuts it down via the token.
    ///
    /// Only singletons are injected here (dispatcher, serializer, manager) &mdash; never a
    /// scoped service, which would be a captive dependency.
    /// </summary>
    public sealed class GameServerHostedService : BackgroundService
    {
        private readonly PacketDispatcher _dispatcher;
        private readonly IPacketSerializer _serializer;
        private readonly ConnectionManager _connections;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<GameServerHostedService> _logger;
        private readonly int _port;

        private TcpListener? _listener;

        public GameServerHostedService(
            PacketDispatcher dispatcher,
            IPacketSerializer serializer,
            ConnectionManager connections,
            IConfiguration config,
            ILoggerFactory loggerFactory)
        {
            _dispatcher = dispatcher;
            _serializer = serializer;
            _connections = connections;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<GameServerHostedService>();
            _port = config.GetValue("GameServer:Port", 6969);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _logger.LogInformation("GameServer listening on port {Port}", _port);

            var connectionLogger = _loggerFactory.CreateLogger<Connection>();

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    TcpClient client = await _listener.AcceptTcpClientAsync(stoppingToken);

                    var connection = new Connection(
                        client, _dispatcher, _serializer, _connections, connectionLogger);

                    // Fire-and-forget: each connection runs its own loop concurrently.
                    _ = connection.RunAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // normal shutdown
            }
            finally
            {
                _listener.Stop();
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("GameServer stopping; closing {Count} connection(s)", _connections.Count);
            foreach (var connection in _connections.Connections)
                connection.Close();

            _listener?.Stop();
            await base.StopAsync(cancellationToken);
        }
    }
}
