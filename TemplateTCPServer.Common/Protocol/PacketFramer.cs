using System.Buffers.Binary;

namespace TemplateTCPServer.Common.Protocol
{
    // Wire format: [4-byte big-endian payload length][2-byte big-endian MsgId][payload].
    public static class PacketFramer
    {
        public const int HeaderLength = sizeof(int) + sizeof(ushort);

        public static BasePacket? Read(Stream stream, IPacketSerializer serializer)
        {
            var header = new byte[HeaderLength];
            if (!ReadExactlyOrEof(stream, header))
                return null;

            int payloadLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, sizeof(int)));
            var msgId = (MsgId)BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(sizeof(int), sizeof(ushort)));

            if (payloadLength < 0)
                throw new InvalidDataException($"Negative payload length ({payloadLength}) in frame.");

            var payload = new byte[payloadLength];
            if (payloadLength > 0 && !ReadExactlyOrEof(stream, payload))
                throw new EndOfStreamException("Stream ended mid-payload.");

            return serializer.Deserialize(msgId, payload);
        }

        public static void Write(Stream stream, IPacketSerializer serializer, BasePacket packet)
        {
            ReadOnlyMemory<byte> payload = serializer.Serialize(packet);

            var header = new byte[HeaderLength];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, sizeof(int)), payload.Length);
            BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(sizeof(int), sizeof(ushort)), (ushort)packet.MsgId);

            stream.Write(header);
            if (!payload.IsEmpty)
                stream.Write(payload.Span);
            stream.Flush();
        }

        // Returns false on a clean EOF at a frame boundary; throws if the stream ends mid-frame.
        private static bool ReadExactlyOrEof(Stream stream, Span<byte> buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer[read..]);
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
