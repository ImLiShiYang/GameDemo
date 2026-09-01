using System;

[Serializable]
public struct LockstepInput
{
    public const int AxisScale = 1000;

    public int horizontal;
    public int vertical;

    public LockstepInput(int horizontal, int vertical)
    {
        this.horizontal = horizontal;
        this.vertical = vertical;
    }
}
