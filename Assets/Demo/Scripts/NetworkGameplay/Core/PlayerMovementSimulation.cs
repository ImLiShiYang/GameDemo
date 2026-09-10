using UnityEngine;

public enum PlayerHitKind : byte
{
    None = 0,
    Normal = 1,
    Heavy = 2,
    Lethal = 3
}

/// <summary>
/// 客户端预测与服务器权威模拟共用的固定 Tick 玩家移动算法。
/// 核心 Step 只计算期望位移和动作状态；两端交给 NetworkCharacterMotor 执行实际运动。
/// </summary>
public static class PlayerMovementSimulation
{
#if NETWORK_PLAYER_MIGRATION_CHECKS
    public static float MoveSpeed => 3.2f;
    public static float Acceleration => 18f;
    public static float Deceleration => 22f;
    public static int RollDurationTicks => 15;
    public static int RollCooldownTicks => 7;
    public static float RollDistance => 4f;
    public static int NormalHitStunTicks => 3;
    public static float DeathImpactDuration => 0.08f;
    public static int HeavyHitTicks => 6;
    public static float HeavyHitDistance => 1.5f;
#else
    public static float MoveSpeed => PlayerMotionProfile.Runtime.MoveSpeed;
    public static float Acceleration => PlayerMotionProfile.Runtime.Acceleration;
    public static float Deceleration => PlayerMotionProfile.Runtime.Deceleration;
    public static int RollDurationTicks => PlayerMotionProfile.Runtime.RollDurationTicks;
    public static int RollCooldownTicks => PlayerMotionProfile.Runtime.RollCooldownTicks;
    public static float RollDistance => PlayerMotionProfile.Runtime.RollDistance;
    public static int NormalHitStunTicks => PlayerMotionProfile.Runtime.NormalHitStunTicks;
    public static float DeathImpactDuration => PlayerMotionProfile.Runtime.DeathImpactDuration;
    public static int HeavyHitTicks => PlayerMotionProfile.Runtime.HeavyHitTicks;
    public static float HeavyHitDistance => PlayerMotionProfile.Runtime.HeavyHitDistance;
#endif
    public static float TickDeltaTime => 1f / NetworkRuntime.DefaultTickRate;

    public static void Step(ref Vector3 position, ref float rotationY, ref PlayerActionState action,
        Vector2 move, Vector2 aim, ClientInputButtons buttons, bool actionsAllowed, uint simulationTick = 0)
    {
        Step(ref position, ref rotationY, ref action, move, aim, buttons, actionsAllowed,
            MoveSpeed, Acceleration, Deceleration, simulationTick);
    }

    public static void Step(ref Vector3 position, ref float rotationY, ref PlayerActionState action,
        Vector2 move, Vector2 aim, ClientInputButtons buttons, bool actionsAllowed, float moveSpeed, float acceleration,
        uint simulationTick = 0)
    {
        Step(ref position, ref rotationY, ref action, move, aim, buttons, actionsAllowed,
            moveSpeed, acceleration, Deceleration, simulationTick);
    }

    public static void Step(ref Vector3 position, ref float rotationY, ref PlayerActionState action,
        Vector2 move, Vector2 aim, ClientInputButtons buttons, bool actionsAllowed, float moveSpeed, float acceleration,
        float deceleration, uint simulationTick)
    {
        action.RollTicks = Mathf.Max(0, action.RollTicks - 1);
        action.RollCooldownTicks = Mathf.Max(0, action.RollCooldownTicks - 1);
        int previousHitTicks = action.HitStunTicks;
        action.HitStunTicks = Mathf.Max(0, action.HitStunTicks - 1);
        if (!actionsAllowed || previousHitTicks > 0)
        {
            action.RollTicks = 0;
            action.MoveDirection = Vector2.zero;
            if (actionsAllowed && action.HitKind == PlayerHitKind.Heavy)
                ApplyHeavyHitStep(ref position, ref action, previousHitTicks);
            return;
        }

        if (action.RollTicks > 0)
        {
            ApplyRollStep(ref position, ref rotationY, ref action);
            return;
        }

        if ((buttons & ClientInputButtons.Roll) != 0 && action.RollCooldownTicks == 0)
        {
            Vector2 direction = move.sqrMagnitude >= 0.0025f ? move : aim;
            if (direction.sqrMagnitude < 0.0001f)
            {
                float radians = rotationY * Mathf.Deg2Rad;
                direction = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
            }
            action.RollDirection = direction.normalized;
            action.RollTicks = RollDurationTicks;
            action.RollCooldownTicks = RollDurationTicks + RollCooldownTicks;
            action.RollSequence++;
            action.RollStartTick = simulationTick;
            action.MoveDirection = Vector2.zero;
            ApplyRollStep(ref position, ref rotationY, ref action);
            return;
        }

        Vector2 target = Vector2.ClampMagnitude(move, 1f);
        float changeRate = target.sqrMagnitude > action.MoveDirection.sqrMagnitude ? acceleration : deceleration;
        action.MoveDirection = Vector2.MoveTowards(action.MoveDirection, target, Mathf.Max(0f, changeRate) * TickDeltaTime);
        position += new Vector3(action.MoveDirection.x, 0f, action.MoveDirection.y) * (Mathf.Max(0f, moveSpeed) * TickDeltaTime);
        if (aim.sqrMagnitude > 0.0001f) rotationY = Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg;
    }

    public static void ApplyHit(ref PlayerActionState action, Vector2 direction, PlayerHitKind kind)
    {
        action.RollTicks = 0;
        action.MoveDirection = Vector2.zero;
        action.HitSequence++;
        action.HitDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
        action.HitKind = kind;
        action.HitStunTicks = kind == PlayerHitKind.Lethal ? Mathf.CeilToInt(DeathImpactDuration / TickDeltaTime) :
            kind == PlayerHitKind.Heavy ? HeavyHitTicks : NormalHitStunTicks;
    }

    private static void ApplyRollStep(ref Vector3 position, ref float rotationY, ref PlayerActionState action)
    {
        int completedTicks = RollDurationTicks - action.RollTicks;
        float from = EvaluateRollDistance(completedTicks / (float)RollDurationTicks);
        float to = EvaluateRollDistance((completedTicks + 1) / (float)RollDurationTicks);
        Vector2 direction = action.RollDirection.sqrMagnitude > 0.0001f ? action.RollDirection.normalized : Vector2.up;
        rotationY = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
        position += new Vector3(direction.x, 0f, direction.y) * (RollDistance * Mathf.Max(0f, to - from));
    }

    private static void ApplyHeavyHitStep(ref Vector3 position, ref PlayerActionState action, int remainingTicks)
    {
        int completedTicks = HeavyHitTicks - remainingTicks;
        float from = EvaluateHeavyHitDistance(completedTicks / (float)HeavyHitTicks);
        float to = EvaluateHeavyHitDistance((completedTicks + 1) / (float)HeavyHitTicks);
        Vector2 direction = action.HitDirection.sqrMagnitude > 0.0001f ? action.HitDirection.normalized : Vector2.down;
        position += new Vector3(direction.x, 0f, direction.y) * (HeavyHitDistance * Mathf.Max(0f, to - from));
    }

    private static float EvaluateRollDistance(float phase)
    {
#if NETWORK_PLAYER_MIGRATION_CHECKS
        float[] samples = { 0f, 0.018f, 0.058f, 0.119f, 0.198f, 0.292f, 0.397f, 0.508f,
            0.62f, 0.726f, 0.82f, 0.897f, 0.953f, 0.985f, 0.998f, 1f };
        float position = Mathf.Clamp01(phase) * (samples.Length - 1);
        int from = Mathf.Min(Mathf.FloorToInt(position), samples.Length - 2);
        return Mathf.Lerp(samples[from], samples[from + 1], position - from);
#else
        return PlayerMotionProfile.Runtime.EvaluateRollDistance(phase);
#endif
    }

    private static float EvaluateHeavyHitDistance(float phase)
    {
#if NETWORK_PLAYER_MIGRATION_CHECKS
        phase = Mathf.Clamp01(phase);
        return phase < 0.35f ? Mathf.Lerp(0f, 0.8f, phase / 0.35f) : Mathf.Lerp(0.8f, 1f, (phase - 0.35f) / 0.65f);
#else
        return PlayerMotionProfile.Runtime.EvaluateHeavyHitDistance(phase);
#endif
    }

    public static void Step(ref Vector3 position, ref float rotationY, Vector2 moveInput, Vector2 aimInput)
    {
        Vector2 movement = Vector2.ClampMagnitude(moveInput, 1f);
        position += new Vector3(movement.x, 0f, movement.y) * (MoveSpeed * TickDeltaTime);

        Vector2 aim = Vector2.ClampMagnitude(aimInput, 1f);

        if (aim.sqrMagnitude > 0.0001f)
        {
            rotationY = Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg;
        }
    }
}

/// <summary>必须随权威快照恢复的动作状态；重演不触发音效、动画或伤害。</summary>
public struct PlayerActionState
{
    public int RollTicks;
    public int RollCooldownTicks;
    public int HitStunTicks;
    public Vector2 RollDirection;
    public Vector2 MoveDirection;
    public uint RollSequence;
    public uint RollStartTick;
    public uint HitSequence;
    public Vector2 HitDirection;
    public PlayerHitKind HitKind;
    public bool IsRolling => RollTicks > 0;
    public bool IsInvincible => IsRolling;
    public float RollNormalizedTime => 1f - RollTicks / (float)Mathf.Max(1, PlayerMovementSimulation.RollDurationTicks);
}
