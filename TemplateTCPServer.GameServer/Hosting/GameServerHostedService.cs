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
    public sealed class GameServerHostedService : IHostedService
    {
        private readonly PacketDispatcher _dispatcher;
        private readonly IPacketSerializer _serializer;
        private readonly ConnectionManager _connections;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<GameServerHostedService> _logger;
        private readonly int _port;

        private TcpListener? _listener;
        private Thread? _acceptThread;
        private volatile bool _stopping;

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

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _listener = new TcpListener(IPAddress.Any, _port);
            _listener.Start();
            _logger.LogInformation("GameServer listening on port {Port}", _port);

            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "GameServer-Accept" };
            _acceptThread.Start();
            return Task.CompletedTask;
        }

        private void AcceptLoop()
        {
            var connectionLogger = _loggerFactory.CreateLogger<Connection>();

            try
            {
                while (!_stopping)
                {
                    TcpClient client = _listener!.AcceptTcpClient();

                    var connection = new Connection(
                        client, _dispatcher, _serializer, _connections, connectionLogger);

                    var thread = new Thread(connection.Run) { IsBackground = true };
                    thread.Start();
                }
            }
            catch (SocketException) when (_stopping)
            {
                // listener stopped during shutdown
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _stopping = true;

            _logger.LogInformation("GameServer stopping; closing {Count} connection(s)", _connections.Count);
            foreach (var connection in _connections.Connections)
                connection.Close();

            _listener?.Stop();
            return Task.CompletedTask;
        }
    }
}
