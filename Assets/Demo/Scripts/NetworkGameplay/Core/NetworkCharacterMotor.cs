using UnityEngine;

public struct CharacterMotorState
{
    public Vector3 Position;
    public float VerticalVelocity;
    public bool Grounded;
}

public readonly struct NetworkCharacterShape
{
    public readonly float Radius;
    public readonly float Height;
    public NetworkCharacterShape(float radius, float height) { Radius = radius; Height = height; }
    public static NetworkCharacterShape ForPrefab(int prefabId)
    {
        if (prefabId == NetworkPrefabCatalog.BossPrefabId) return new NetworkCharacterShape(0.9f, 4.4f);
        if (prefabId == NetworkPrefabCatalog.TestEnemyPrefabId) return new NetworkCharacterShape(0.45f, 2.4f);
        return new NetworkCharacterShape(0.5f, 2f);
    }
}

/// <summary>独立逻辑身体。没有 Update、动画、伤害或触发器回调；只由固定网络 Tick 驱动。</summary>
public sealed class NetworkCharacterMotor : MonoBehaviour
{
    public const float Gravity = -9.81f;
    public const float GroundedVelocity = -2f;
    public const float TerminalSpeed = 40f;
    public const float ContactMargin = 0.02f;
    public int EntityId { get; private set; }
    public int PrefabId { get; private set; }
    public NetworkCharacterShape Shape { get; private set; }
    public CharacterMotorState State { get; private set; }
    public CharacterController Controller { get; private set; }
    public bool Blocking { get; private set; } = true;
    private NetworkCharacterWorld world;
    private CapsuleCollider proxy;

    public void Initialize(NetworkCharacterWorld owner, int entityId, int prefabId, Vector3 position, bool simulated)
    {
        world = owner;
        EntityId = entityId;
        PrefabId = prefabId;
        Shape = NetworkCharacterShape.ForPrefab(prefabId);
        // 不污染瞄准射线；角色接触使用 world 的逻辑胶囊，不依赖全局 Layer 矩阵。
        gameObject.layer = 2;
        transform.position = position;
        if (simulated)
        {
            Controller = gameObject.AddComponent<CharacterController>();
            Controller.radius = Shape.Radius;
            Controller.height = Shape.Height;
            Controller.center = Vector3.up * (Shape.Height * 0.5f);
            Controller.slopeLimit = 45f;
            Controller.stepOffset = 0.3f;
            Controller.skinWidth = 0.08f;
            Controller.minMoveDistance = 0f;
        }
        else
        {
            proxy = gameObject.AddComponent<CapsuleCollider>();
            proxy.radius = Shape.Radius;
            proxy.height = Shape.Height;
            proxy.center = Vector3.up * (Shape.Height * 0.5f);
            proxy.isTrigger = true;
        }
        State = new CharacterMotorState { Position = position };
    }

    public void SetBlocking(bool blocking)
    {
        Blocking = blocking;
        if (proxy != null) proxy.enabled = blocking;
        // 死亡身体仍可落地，但不再参与其他角色的运动阻挡。
    }

    public void Restore(CharacterMotorState state)
    {
        // 传送必须重置 CC 内部接触缓存；绝不传送可见模型。
        if (Controller != null) Controller.enabled = false;
        transform.position = state.Position;
        State = state;
        if (Controller != null)
        {
            Controller.enabled = true;
            world.IgnoreControllerPairs(this);
        }
    }

    public CharacterMotorState Step(Vector3 horizontalDisplacement, float deltaTime)
    {
        if (Controller == null || deltaTime <= 0f) return State;
        CharacterMotorState state = State;
        state.VerticalVelocity = Mathf.Max(-TerminalSpeed,
            (state.Grounded && state.VerticalVelocity < 0f ? GroundedVelocity : state.VerticalVelocity) + Gravity * deltaTime);
        horizontalDisplacement.y = 0f;
        // 小步也覆盖垂直下降，防止地形滑动或高速下落绕过角色阻挡检查。
        int steps = Mathf.Max(1, Mathf.CeilToInt((horizontalDisplacement.magnitude + Mathf.Abs(state.VerticalVelocity * deltaTime)) / 0.1f));
        Vector3 increment = horizontalDisplacement / steps;
        CollisionFlags flags = CollisionFlags.None;
        for (int i = 0; i < steps; i++)
        {
            Vector3 start = transform.position;
            Vector3 allowed = Blocking ? world.ConstrainActors(this, start, increment) : increment;
            flags = Controller.Move(allowed + Vector3.up * (state.VerticalVelocity * deltaTime / steps));
            // CC 在坡面/墙角可能产生额外侧移，不能因此穿入角色。
            if (Blocking && world.HasActorOverlap(this, transform.position) && !world.HasActorOverlap(this, start))
            {
                CharacterMotorState safe = state;
                safe.Position = start;
                Restore(safe);
                flags = CollisionFlags.Sides;
                break;
            }
            if ((flags & CollisionFlags.Below) != 0 && state.VerticalVelocity < 0f) state.VerticalVelocity = GroundedVelocity;
            if ((flags & CollisionFlags.Above) != 0 && state.VerticalVelocity > 0f) state.VerticalVelocity = 0f;
        }
        state.Position = transform.position;
        state.Grounded = (flags & CollisionFlags.Below) != 0;
        State = state;
        return state;
    }
}
