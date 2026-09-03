using System;
using System.IO;

public static class NetworkPacketCodec
{
    public static NetworkPacket ReadPacket(Stream stream)
    {
        byte[] headerBytes = ReadExactly(stream, NetworkPacketHeader.SerializedSize);
        NetworkPacketHeader header;

        using (MemoryStream headerStream = new MemoryStream(headerBytes, false))
        using (BinaryReader reader = new BinaryReader(headerStream))
        {
            header = new NetworkPacketHeader
            {
                Magic = reader.ReadUInt32(),
                ProtocolVersion = reader.ReadUInt16(),
                MessageType = (NetworkMessageType)reader.ReadUInt16(),
                PayloadLength = reader.ReadInt32(),
                Sequence = reader.ReadUInt32(),
                ServerTick = reader.ReadUInt32(),
                MatchId = reader.ReadInt64()
            };
        }

        ValidateHeader(header);
        return new NetworkPacket(header, ReadExactly(stream, header.PayloadLength));
    }

    public static void WritePacket(Stream stream, NetworkMessageType messageType, byte[] payload, uint sequence, uint serverTick, long matchId)
    {
        payload = payload ?? Array.Empty<byte>();

        if (payload.Length > NetworkPacketHeader.MaximumPayloadLength)
        {
            throw new InvalidDataException($"Payload 长度 {payload.Length} 超过协议限制。");
        }

        using (MemoryStream packetStream = new MemoryStream(NetworkPacketHeader.SerializedSize + payload.Length))
        using (BinaryWriter writer = new BinaryWriter(packetStream))
        {
            writer.Write(NetworkPacketHeader.ExpectedMagic);
            writer.Write(NetworkPacketHeader.CurrentProtocolVersion);
            writer.Write((ushort)messageType);
            writer.Write(payload.Length);
            writer.Write(sequence);
            writer.Write(serverTick);
            writer.Write(matchId);
            writer.Write(payload);
            writer.Flush();

            byte[] packetBytes = packetStream.ToArray();
            stream.Write(packetBytes, 0, packetBytes.Length);
            stream.Flush();
        }
    }

    private static void ValidateHeader(NetworkPacketHeader header)
    {
        if (header.Magic != NetworkPacketHeader.ExpectedMagic)
        {
            throw new InvalidDataException("消息 Magic 不正确。");
        }

        if (header.ProtocolVersion != NetworkPacketHeader.CurrentProtocolVersion)
        {
            throw new InvalidDataException($"不支持的协议版本：{header.ProtocolVersion}。");
        }

        if (!Enum.IsDefined(typeof(NetworkMessageType), header.MessageType))
        {
            throw new InvalidDataException($"未知消息类型：{(ushort)header.MessageType}。");
        }

        if (header.PayloadLength < 0 || header.PayloadLength > NetworkPacketHeader.MaximumPayloadLength)
        {
            throw new InvalidDataException($"非法 Payload 长度：{header.PayloadLength}。");
        }
    }

    private static byte[] ReadExactly(Stream stream, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;

        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);

            if (read <= 0)
            {
                throw new EndOfStreamException("远端已关闭连接。");
            }

            offset += read;
        }

        return buffer;
    }
}
