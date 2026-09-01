public struct LockstepFrame
{
    public int tick;
    public LockstepInput player1Input;
    public LockstepInput player2Input;

    public LockstepFrame(int tick, LockstepInput player1Input, LockstepInput player2Input)
    {
        this.tick = tick;
        this.player1Input = player1Input;
        this.player2Input = player2Input;
    }
}
