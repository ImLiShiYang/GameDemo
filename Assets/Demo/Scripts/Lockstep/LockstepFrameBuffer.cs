using System.Collections.Generic;

public sealed class LockstepFrameBuffer
{
    private sealed class PendingFrame
    {
        public bool hasPlayer1Input;
        public bool hasPlayer2Input;
        public LockstepInput player1Input;
        public LockstepInput player2Input;
    }

    private readonly Dictionary<int, PendingFrame> frames = new Dictionary<int, PendingFrame>();

    public void SubmitFrame(LockstepFrame frame)
    {
        PendingFrame pendingFrame = new PendingFrame
        {
            hasPlayer1Input = true,
            hasPlayer2Input = true,
            player1Input = frame.player1Input,
            player2Input = frame.player2Input
        };
        frames[frame.tick] = pendingFrame;
    }

    public bool TryConsumeFrame(int tick, out LockstepFrame frame)
    {
        frame = default;
        if (!frames.TryGetValue(tick, out PendingFrame pendingFrame)) return false;
        if (!pendingFrame.hasPlayer1Input || !pendingFrame.hasPlayer2Input) return false;

        frame = new LockstepFrame(tick, pendingFrame.player1Input, pendingFrame.player2Input);
        frames.Remove(tick);
        return true;
    }

    public void Clear()
    {
        frames.Clear();
    }
}
