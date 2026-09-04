public struct NetworkPacketHeader
{
    public const uint ExpectedMagic = 0x47444D4F;
    public const ushort CurrentProtocolVersion = 7;
    public const int SerializedSize = 28;
    public const int MaximumPayloadLength = 1024 * 1024;

    public uint Magic;
    public ushort ProtocolVersion;
    public NetworkMessageType MessageType;
    public int PayloadLength;
    public uint Sequence;
    public uint ServerTick;
    public long MatchId;
}

public readonly struct NetworkPacket
{
    public NetworkPacket(NetworkPacketHeader header, byte[] payload)
    {
        Header = header;
        Payload = payload;
    }

    public NetworkPacketHeader Header { get; }
    public byte[] Payload { get; }
}
