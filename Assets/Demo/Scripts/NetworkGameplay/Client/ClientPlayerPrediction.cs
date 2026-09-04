using UnityEngine;

/// <summary>
/// 只预测本地玩家的移动和朝向。服务器状态始终是基线，未确认输入会在基线上重新执行。
/// </summary>
public sealed class ClientPlayerPrediction : MonoBehaviour
{
    private const int InputBufferSize = 128;
    private const float IgnoreCorrectionDistance = 0.02f;
    private const float IgnoreCorrectionAngle = 1f;
    private const float HardSnapDistance = 1f;
    private const float HardSnapAngle = 45f;

    private readonly PredictedInput[] inputBuffer = new PredictedInput[InputBufferSize];

    private Transform playerTransform;
    private NetworkTransformInterpolator presentation;
    private Vector3 predictedPosition;
    private float predictedRotationY;
    private PlayerActionState predictedAction;
    private GrayboxPlayerController playerView;
    private CharacterController collisionShape;
    private bool dead;
    private bool firing;
    private uint latestPredictedSequence;
    private uint lastAcknowledgedSequence;
    private bool initialized;

    public int PendingInputCount { get; private set; }
    public float LastCorrectionDistance { get; private set; }
    public float LastCorrectionAngle { get; private set; }
    public PlayerActionState Action => predictedAction;

    public void Initialize(Transform localPlayerTransform, NetworkTransformInterpolator interpolator)
    {
        playerTransform = localPlayerTransform;
        presentation = interpolator;
        playerView = localPlayerTransform.GetComponent<GrayboxPlayerController>();
        collisionShape = localPlayerTransform.GetComponent<CharacterController>();
        predictedPosition = playerTransform.position;
        predictedRotationY = playerTransform.eulerAngles.y;
        initialized = true;
    }

    public void Predict(ClientInputMessage input, bool movementEnabled)
    {
        if (!initialized || input == null || input.Sequence == 0)
        {
            return;
        }

        PredictedInput predictedInput = new PredictedInput
        {
            Sequence = input.Sequence,
            Move = movementEnabled ? new Vector2(input.Horizontal, input.Vertical) : Vector2.zero,
            Aim = new Vector2(input.AimX, input.AimZ),
            Buttons = input.Buttons,
            Enabled = movementEnabled
        };
        inputBuffer[input.Sequence % InputBufferSize] = predictedInput;
        latestPredictedSequence = input.Sequence;
        Simulate(predictedInput);
        PendingInputCount = CalculatePendingInputCount(lastAcknowledgedSequence, latestPredictedSequence);
        presentation.ApplyPredictedState(predictedPosition, predictedRotationY,
            predictedInput.Move.magnitude * PlayerMovementSimulation.MoveSpeed);
        playerView?.ApplyNetworkMotion(predictedAction, firing, dead);
    }

    public void Reconcile(PlayerNetworkState serverState)
    {
        if (!initialized || serverState == null)
        {
            return;
        }

        uint acknowledgedSequence = serverState.LastProcessedInputSequence;

        if (acknowledgedSequence < lastAcknowledgedSequence)
        {
            return;
        }
        dead = serverState.CurrentHealth <= 0f;
        predictedAction = serverState.Action;
        firing = serverState.IsFiring;

        if (acknowledgedSequence > latestPredictedSequence)
        {
            NetworkLog.Warning($"服务器确认输入 {acknowledgedSequence} 超过客户端最新预测 {latestPredictedSequence}，执行硬校正。");
            lastAcknowledgedSequence = acknowledgedSequence;
            latestPredictedSequence = acknowledgedSequence;
            predictedPosition = serverState.Position;
            predictedRotationY = serverState.RotationY;
            PendingInputCount = 0;
            LastCorrectionDistance = Vector3.Distance(playerTransform.position, predictedPosition);
            LastCorrectionAngle = Mathf.Abs(Mathf.DeltaAngle(playerTransform.eulerAngles.y, predictedRotationY));
            presentation.SnapTo(predictedPosition, predictedRotationY, serverState.MoveSpeed);
            playerView?.ApplyNetworkMotion(predictedAction, firing, dead);
            return;
        }

        Vector3 previousPrediction = predictedPosition;
        float previousPredictedRotationY = predictedRotationY;
        predictedPosition = serverState.Position;
        predictedRotationY = serverState.RotationY;
        if (acknowledgedSequence > lastAcknowledgedSequence)
        {
            lastAcknowledgedSequence = acknowledgedSequence;
        }
        int pendingCount = CalculatePendingInputCount(lastAcknowledgedSequence, latestPredictedSequence);

        if (pendingCount >= InputBufferSize)
        {
            NetworkLog.Warning("客户端未确认输入超过预测缓冲区容量，丢弃历史并使用服务器状态。");
            latestPredictedSequence = lastAcknowledgedSequence;
            pendingCount = 0;
        }
        else
        {
            for (int offset = 1; offset <= pendingCount; offset++)
            {
                uint sequence = lastAcknowledgedSequence + (uint)offset;
                PredictedInput input = inputBuffer[sequence % InputBufferSize];

                if (input.Sequence != sequence)
                {
                    continue;
                }

                Simulate(input);
            }
        }

        PendingInputCount = pendingCount;
        LastCorrectionDistance = Vector3.Distance(previousPrediction, predictedPosition);
        LastCorrectionAngle = Mathf.Abs(Mathf.DeltaAngle(previousPredictedRotationY, predictedRotationY));
        playerView?.ApplyNetworkMotion(predictedAction, firing, dead);

        if (LastCorrectionDistance >= HardSnapDistance || LastCorrectionAngle >= HardSnapAngle)
        {
            presentation.SnapTo(predictedPosition, predictedRotationY, serverState.MoveSpeed);
        }
        else if (LastCorrectionDistance >= IgnoreCorrectionDistance || LastCorrectionAngle >= IgnoreCorrectionAngle)
        {
            presentation.ApplyPredictedState(predictedPosition, predictedRotationY, serverState.MoveSpeed);
        }
    }

    private static int CalculatePendingInputCount(uint acknowledgedSequence, uint latestSequence)
    {
        if (latestSequence <= acknowledgedSequence)
        {
            return 0;
        }

        uint difference = latestSequence - acknowledgedSequence;
        return difference > int.MaxValue ? int.MaxValue : (int)difference;
    }

    private struct PredictedInput
    {
        public uint Sequence;
        public Vector2 Move;
        public Vector2 Aim;
        public ClientInputButtons Buttons;
        public bool Enabled;
    }

    private void Simulate(PredictedInput input)
    {
        Vector3 previousPosition = predictedPosition;
        PlayerMovementSimulation.Step(ref predictedPosition, ref predictedRotationY, ref predictedAction,
            input.Move, input.Aim, input.Buttons, input.Enabled && !dead,
            playerView != null ? playerView.NetworkMoveSpeed : PlayerMovementSimulation.MoveSpeed,
            playerView != null ? playerView.NetworkAcceleration : 18f);
        predictedPosition = PlayerMovementSimulation.ConstrainToWorld(previousPosition, predictedPosition, collisionShape);
        firing = input.Enabled && !dead && !predictedAction.IsRolling && predictedAction.HitStunTicks == 0 &&
            (input.Buttons & ClientInputButtons.Fire) != 0;
    }
}
