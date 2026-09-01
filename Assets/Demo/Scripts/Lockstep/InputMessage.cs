using System;

[Serializable]
public struct InputMessage
{
    public int tick;
    public int playerId;
    public LockstepInput input;

    public InputMessage(int tick, int playerId, LockstepInput input)
    {
        this.tick = tick;
        this.playerId = playerId;
        this.input = input;
    }
}
