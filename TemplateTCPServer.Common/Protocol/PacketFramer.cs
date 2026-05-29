using System.Buffers.Binary;

namespace TemplateTCPServer.Common.Protocol
{
    /// <summary>
    /// Reads and writes length-prefixed frames on a stream. Wire format:
    /// <code>[4-byte big-endian payload length][2-byte big-endian MsgId][payload bytes]</code>
    /// This replaces the old <c>BinaryReader.ReadString()</c> + <c>DataAvailable</c> busy
    /// loop: a frame is fully assembled before it is handed to the dispatcher.
    /// </summary>
    public static class PacketFramer
    {
        /// <summary>Header is a 4-byte length followed by a 2-byte MsgId.</summary>
        public const int HeaderLength = sizeof(int) + sizeof(ushort);

        /// <summary>
        /// Reads one complete frame from <paramref name="stream"/> and deserializes it.
        /// Returns <c>null</c> when the stream reaches end-of-stream cleanly (peer closed).
        /// </summary>
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

        /// <summary>Serializes a packet and writes one complete frame to the stream.</summary>
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

        /// <summary>
        /// Fills <paramref name="buffer"/> completely. Returns <c>false</c> if the stream
        /// is at end-of-stream before any byte is read (clean close); throws if it ends
        /// part-way through.
        /// </summary>
        private static async Task<bool> ReadExactlyOrEofAsync(
            Stream stream, Memory<byte> buffer, CancellationToken ct)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = await stream.ReadAsync(buffer[read..], ct);
                if (n == 0)
                {
                    if (read == 0) return false;      // clean EOF on a frame boundary
                    throw new EndOfStreamException("Stream ended mid-frame.");
                }
                read += n;
            }
            return true;
        }
    }
}
