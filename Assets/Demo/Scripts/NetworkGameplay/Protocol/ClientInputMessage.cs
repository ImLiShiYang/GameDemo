using System;

[Flags]
public enum ClientInputButtons : byte
{
    None = 0,
    Fire = 1 << 0,
    Roll = 1 << 1,
    Skill1 = 1 << 2,
    Interact = 1 << 3
}

public sealed class ClientInputMessage
{
    public uint Sequence;
    public uint ClientTick;
    public float Horizontal;
    public float Vertical;
    public float AimX;
    public float AimZ;
    public ClientInputButtons Buttons;
}
