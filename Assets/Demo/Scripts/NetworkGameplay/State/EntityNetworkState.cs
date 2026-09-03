using UnityEngine;

/// <summary>
/// 非玩家网络实体在世界快照中的通用状态。创建和删除仍由可靠的 Spawn/Despawn 消息负责。
/// </summary>
public sealed class EntityNetworkState
{
    public int EntityId;
    public NetworkEntityType EntityType;
    public int PrefabId;
    public int OwnerPlayerId;
    public Vector3 Position;
    public float RotationY;
    public float CurrentHealth;
    public float MaxHealth;
    public byte AnimationState;
    public int TargetEntityId;
}
