using UnityEngine;

/// <summary>
/// 客户端子弹只负责显示：本地按服务器速度连续飞行，快照仅用于纠偏。
/// 本组件没有碰撞和伤害逻辑，命中结果始终以服务器的 Despawn/BattleEvent 为准。
/// </summary>
public sealed class ClientProjectileView : MonoBehaviour
{
    private const float CorrectionRate = 12f;
    private const float SnapDistance = 2.5f;

    private Vector3 velocity;
    private Vector3 authoritativePosition;
    private bool initialized;

    public void Initialize(EntitySpawnMessage message, uint packetServerTick)
    {
        velocity = message.Velocity;
        uint currentTick = packetServerTick > NetworkRuntime.ServerTick ? packetServerTick : NetworkRuntime.ServerTick;
        uint elapsedTicks = currentTick >= message.SpawnTick ? currentTick - message.SpawnTick : 0;
        float elapsedSeconds = elapsedTicks / (float)NetworkRuntime.DefaultTickRate;
        authoritativePosition = message.Position + velocity * elapsedSeconds;
        transform.SetPositionAndRotation(authoritativePosition, message.Rotation);
        initialized = true;
    }

    public void ApplyState(EntityNetworkState state)
    {
        velocity = state.Velocity;
        authoritativePosition = state.Position;

        if (!initialized || Vector3.Distance(transform.position, authoritativePosition) > SnapDistance)
        {
            transform.position = authoritativePosition;
        }

        initialized = true;
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        authoritativePosition += velocity * deltaTime;
        transform.position += velocity * deltaTime;
        float blend = 1f - Mathf.Exp(-CorrectionRate * deltaTime);
        transform.position = Vector3.Lerp(transform.position, authoritativePosition, blend);

        if (velocity.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
        }
    }
}
