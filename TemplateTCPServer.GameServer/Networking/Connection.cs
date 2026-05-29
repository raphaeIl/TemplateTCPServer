using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.GameServer.Packets;

namespace TemplateTCPServer.GameServer.Networking
{
    /// <summary>
    /// One live client connection. Owns the socket and the read loop; lives as long as the
    /// client is connected. <b>Not</b> a DI service &mdash; it is <c>new</c>'d per accepted
    /// socket and only ever references singletons (dispatcher, serializer, manager). All
    /// scoped work (DbContext, repositories) happens inside the per-packet scope the
    /// dispatcher creates, never here.
    ///
    /// Per-connection session state (e.g. the authenticated account) belongs on this object,
    /// not in the DI scope, because the scope is per-packet.
    /// </summary>
    public sealed class Connection
    {
        private readonly TcpClient _client;
        private readonly NetworkStream _stream;
        private readonly PacketDispatcher _dispatcher;
        private readonly IPacketSerializer _serializer;
        private readonly ConnectionManager _manager;
        private readonly ILogger _logger;

        public string Id { get; }

        public Connection(
            TcpClient client,
            PacketDispatcher dispatcher,
            IPacketSerializer serializer,
            ConnectionManager manager,
            ILogger logger)
        {
            _client = client;
            _stream = client.GetStream();
            _dispatcher = dispatcher;
            _serializer = serializer;
            _manager = manager;
            _logger = logger;
            Id = client.Client.RemoteEndPoint!.ToString()!;
        }

        /// <summary>
        /// The connection's read loop: frame in, dispatch (handled in its own per-packet
        /// DI scope), repeat until the peer closes or the server shuts down.
        /// </summary>
        public async Task RunAsync(CancellationToken ct)
        {
            _manager.Add(this);
            _logger.LogInformation("{Id} connected", Id);

            try
            {
                while (!ct.IsCancellationRequested)
                {
                    BasePacket? packet = await PacketFramer.ReadAsync(_stream, _serializer, ct);
                    if (packet is null)
                        break; // peer closed cleanly

                    await _dispatcher.DispatchAsync(this, packet, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // server shutting down
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{Id} read loop ended", Id);
            }
            finally
            {
                _manager.Remove(this);
                _client.Close();
                _logger.LogInformation("{Id} disconnected", Id);
            }
        }

        /// <summary>Serializes and writes a packet back to this client.</summary>
        public Task SendAsync(BasePacket packet, CancellationToken ct = default)
            => PacketFramer.WriteAsync(_stream, _serializer, packet, ct);

        public void Close() => _client.Close();
    }
}
