using UnityEngine;

/// <summary>
/// 客户端预测与服务器权威模拟共用的固定 Tick 玩家移动算法。
/// 核心 Step 只计算期望位移和动作状态；两端交给 NetworkCharacterMotor 执行实际运动。
/// </summary>
public static class PlayerMovementSimulation
{
    public const float MoveSpeed = 3.2f;
    // Running Dive Roll: 44 frames at 30 FPS, Animator speed 2; quantized to 20 Hz.
    public const int RollDurationTicks = 15;
    public const int RollCooldownTicks = 7;
    public const float RollDistance = 4f;
    public static float TickDeltaTime => 1f / NetworkRuntime.DefaultTickRate;
    public static void Step(ref Vector3 position, ref float rotationY, ref PlayerActionState action,
        Vector2 move, Vector2 aim, ClientInputButtons buttons, bool actionsAllowed, float moveSpeed = MoveSpeed, float acceleration = 18f)
    {
        action.RollTicks = Mathf.Max(0, action.RollTicks - 1);
        action.RollCooldownTicks = Mathf.Max(0, action.RollCooldownTicks - 1);
        action.HitStunTicks = Mathf.Max(0, action.HitStunTicks - 1);
        if (!actionsAllowed || action.HitStunTicks > 0)
        {
            action.RollTicks = 0;
            action.MoveDirection = Vector2.zero;
            return;
        }

        if ((buttons & ClientInputButtons.Roll) != 0 && action.RollTicks == 0 && action.RollCooldownTicks == 0)
        {
            Vector2 direction = move.sqrMagnitude >= 0.0025f ? move : aim;
            if (direction.sqrMagnitude < 0.0001f)
            {
                float radians = rotationY * Mathf.Deg2Rad;
                direction = new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
            }
            action.RollDirection = direction.normalized;
            action.RollTicks = RollDurationTicks;
            action.RollCooldownTicks = RollCooldownTicks;
        }

        if (action.RollTicks > 0)
        {
            action.MoveDirection = Vector2.zero;
            rotationY = Mathf.Atan2(action.RollDirection.x, action.RollDirection.y) * Mathf.Rad2Deg;
            position += new Vector3(action.RollDirection.x, 0f, action.RollDirection.y) * (RollDistance / RollDurationTicks);
            return;
        }

        action.MoveDirection = Vector2.MoveTowards(action.MoveDirection, Vector2.ClampMagnitude(move, 1f), Mathf.Max(0f, acceleration) * TickDeltaTime);
        position += new Vector3(action.MoveDirection.x, 0f, action.MoveDirection.y) * (Mathf.Max(0f, moveSpeed) * TickDeltaTime);
        if (aim.sqrMagnitude > 0.0001f) 
            rotationY = Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg;
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
    public bool IsRolling => RollTicks > 0;
    public bool IsInvincible => IsRolling;
}
