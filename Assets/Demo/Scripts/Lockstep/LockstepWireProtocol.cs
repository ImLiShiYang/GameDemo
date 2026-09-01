using System.IO;

public static class LockstepWireProtocol
{
    private const int InputMagic = 0x4C53494E;
    private const int FrameMagic = 0x4C534652;
    private const int InputMessageSize = 20;
    private const int FrameMessageSize = 24;

    public static void WriteInput(Stream stream, InputMessage message)
    {
        byte[] buffer = new byte[InputMessageSize];
        WriteInt32(buffer, 0, InputMagic);
        WriteInt32(buffer, 4, message.tick);
        WriteInt32(buffer, 8, message.playerId);
        WriteInt32(buffer, 12, message.input.horizontal);
        WriteInt32(buffer, 16, message.input.vertical);
        stream.Write(buffer, 0, buffer.Length);
    }

    public static bool TryReadInput(Stream stream, out InputMessage message)
    {
        byte[] buffer = new byte[InputMessageSize];
        message = default;
        if (!TryReadExact(stream, buffer)) return false;
        if (ReadInt32(buffer, 0) != InputMagic) throw new InvalidDataException("Received an invalid input message.");

        message = new InputMessage(ReadInt32(buffer, 4), ReadInt32(buffer, 8),
            new LockstepInput(ReadInt32(buffer, 12), ReadInt32(buffer, 16)));
        return true;
    }

    public static void WriteFrame(Stream stream, LockstepFrame frame)
    {
        byte[] buffer = new byte[FrameMessageSize];
        WriteInt32(buffer, 0, FrameMagic);
        WriteInt32(buffer, 4, frame.tick);
        WriteInt32(buffer, 8, frame.player1Input.horizontal);
        WriteInt32(buffer, 12, frame.player1Input.vertical);
        WriteInt32(buffer, 16, frame.player2Input.horizontal);
        WriteInt32(buffer, 20, frame.player2Input.vertical);
        stream.Write(buffer, 0, buffer.Length);
    }

    public static bool TryReadFrame(Stream stream, out LockstepFrame frame)
    {
        byte[] buffer = new byte[FrameMessageSize];
        frame = default;
        if (!TryReadExact(stream, buffer)) return false;
        if (ReadInt32(buffer, 0) != FrameMagic) throw new InvalidDataException("Received an invalid frame message.");

        LockstepInput player1Input = new LockstepInput(ReadInt32(buffer, 8), ReadInt32(buffer, 12));
        LockstepInput player2Input = new LockstepInput(ReadInt32(buffer, 16), ReadInt32(buffer, 20));
        frame = new LockstepFrame(ReadInt32(buffer, 4), player1Input, player2Input);
        return true;
    }

    private static bool TryReadExact(Stream stream, byte[] buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int bytesRead = stream.Read(buffer, offset, buffer.Length - offset);
            if (bytesRead == 0) return false;
            offset += bytesRead;
        }

        return true;
    }

    private static void WriteInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)value;
        buffer[offset + 1] = (byte)(value >> 8);
        buffer[offset + 2] = (byte)(value >> 16);
        buffer[offset + 3] = (byte)(value >> 24);
    }

    private static int ReadInt32(byte[] buffer, int offset)
    {
        return buffer[offset] | buffer[offset + 1] << 8 | buffer[offset + 2] << 16 | buffer[offset + 3] << 24;
    }
}
