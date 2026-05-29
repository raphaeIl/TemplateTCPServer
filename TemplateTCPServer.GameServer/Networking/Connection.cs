using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.GameServer.Packets;

namespace TemplateTCPServer.GameServer.Networking
{
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
                        break;

                    await _dispatcher.DispatchAsync(this, packet, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
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

        public Task SendAsync(BasePacket packet, CancellationToken ct = default)
            => PacketFramer.WriteAsync(_stream, _serializer, packet, ct);

        public void Close() => _client.Close();
    }
}
