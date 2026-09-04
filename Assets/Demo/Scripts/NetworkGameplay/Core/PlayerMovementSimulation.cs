using UnityEngine;

/// <summary>
/// 客户端预测与服务器权威模拟共用的固定 Tick 玩家移动算法。
/// 核心 Step 不读取设备、不访问物理系统，也不播放表现；静态碰撞由两端调用同一 ConstrainToWorld。
/// </summary>
public static class PlayerMovementSimulation
{
    public const float MoveSpeed = 3.2f;
    // Running Dive Roll: 44 frames at 30 FPS, Animator speed 2; quantized to 20 Hz.
    public const int RollDurationTicks = 15;
    public const int RollCooldownTicks = 7;
    public const float RollDistance = 4f;
    public static float TickDeltaTime => 1f / NetworkRuntime.DefaultTickRate;
    private static readonly RaycastHit[] WorldHits = new RaycastHit[32];

    /// <summary>只与静态场景碰撞；角色/敌人的网络位置不参与预测碰撞，避免重演依赖未来状态。</summary>
    public static Vector3 ConstrainToWorld(Vector3 start, Vector3 desired, CharacterController shape)
    {
        if (shape == null) return desired;
        Vector3 scale = shape.transform.lossyScale;
        float radius = Mathf.Max(0.05f, shape.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
        float halfSegment = Mathf.Max(0f, shape.height * Mathf.Abs(scale.y) * 0.5f - radius);
        Vector3 centerOffset = Vector3.Scale(shape.center, scale);
        Vector3 remaining = desired - start;
        Vector3 result = start;
        for (int pass = 0; pass < 2 && remaining.sqrMagnitude > 0.000001f; pass++)
        {
            float distance = remaining.magnitude;
            Vector3 direction = remaining / distance;
            Vector3 center = result + centerOffset;
            int count = Physics.CapsuleCastNonAlloc(center + Vector3.up * halfSegment, center - Vector3.up * halfSegment,
                radius, direction, WorldHits, distance + 0.02f, Physics.AllLayers, QueryTriggerInteraction.Ignore);
            float nearest = distance + 0.02f;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = WorldHits[i];
                if (hit.collider == null || hit.collider.GetComponentInParent<NetworkEntity>() != null ||
                    hit.collider.attachedRigidbody != null || hit.normal.y > 0.7f || hit.distance >= nearest) continue;
                nearest = hit.distance;
                normal = hit.normal;
            }
            if (normal == Vector3.zero) { result += remaining; break; }
            float travel = Mathf.Clamp(nearest - 0.02f, 0f, distance);
            result += direction * travel;
            remaining = Vector3.ProjectOnPlane(direction * (distance - travel), normal);
            remaining.y = 0f;
        }
        return result;
    }

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
