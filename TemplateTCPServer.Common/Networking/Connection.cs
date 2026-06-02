using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using TemplateTCPServer.Common.Protocol;
using TemplateTCPServer.Common.Packets;

namespace TemplateTCPServer.Common.Networking
{
    public sealed class Connection(
        TcpClient client,
        PacketDispatcher dispatcher,
        IPacketSerializer serializer,
        ConnectionManager connectionManager,
        ILogger logger)
    {
        private readonly NetworkStream _stream = client.GetStream();

        public string Id { get; } = client.Client.RemoteEndPoint!.ToString()!;

        public void Run()
        {
            connectionManager.Add(this);
            logger.LogInformation("{Id} connected", Id);

            try
            {
                while (true)
                {
                    BasePacket? packet = PacketFramer.Read(_stream, serializer);
                    if (packet is null)
                        break;

                    dispatcher.Dispatch(this, packet);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "{Id} read loop ended", Id);
            }
            finally
            {
                connectionManager.Remove(this);
                client.Close();
                logger.LogInformation("{Id} disconnected", Id);
            }
        }

        public void Send(BasePacket packet)
            => PacketFramer.Write(_stream, serializer, packet);

        public void Close() => client.Close();
    }
}
