using System.Buffers.Binary;

namespace TemplateTCPServer.Common.Protocol
{
    // Wire format: [4-byte big-endian payload length][2-byte big-endian MsgId][payload].
    public static class PacketFramer
    {
        public const int HeaderLength = sizeof(int) + sizeof(ushort);

        public static async Task<BasePacket?> ReadAsync(
            Stream stream,
            IPacketSerializer serializer,
            CancellationToken ct = default)
        {
            var header = new byte[HeaderLength];
            if (!await ReadExactlyOrEofAsync(stream, header, ct))
                return null;

            int payloadLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, sizeof(int)));
            var msgId = (MsgId)BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(sizeof(int), sizeof(ushort)));

            if (payloadLength < 0)
                throw new InvalidDataException($"Negative payload length ({payloadLength}) in frame.");

            var payload = new byte[payloadLength];
            if (payloadLength > 0 && !await ReadExactlyOrEofAsync(stream, payload, ct))
                throw new EndOfStreamException("Stream ended mid-payload.");

            return serializer.Deserialize(msgId, payload);
        }

        public static async Task WriteAsync(
            Stream stream,
            IPacketSerializer serializer,
            BasePacket packet,
            CancellationToken ct = default)
        {
            ReadOnlyMemory<byte> payload = serializer.Serialize(packet);

            var header = new byte[HeaderLength];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, sizeof(int)), payload.Length);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(sizeof(int), sizeof(ushort)), (ushort)packet.MsgId);

            await stream.WriteAsync(header, ct);
            if (!payload.IsEmpty)
                await stream.WriteAsync(payload, ct);
            await stream.FlushAsync(ct);
        }

        // Returns false on a clean EOF at a frame boundary; throws if the stream ends mid-frame.
        private static async Task<bool> ReadExactlyOrEofAsync(
            Stream stream, Memory<byte> buffer, CancellationToken ct)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer[read..], ct);
                if (n == 0)
                {
                    if (read == 0) return false;
                    throw new EndOfStreamException("Stream ended mid-frame.");
                }
                read += n;
            }
            return true;
        }
    }
}
