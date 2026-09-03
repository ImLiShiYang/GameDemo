using UnityEngine;

public sealed class EntitySpawnMessage
{
    public int EntityId;
    public NetworkEntityType EntityType;
    public int PrefabId;
    public int OwnerPlayerId;
    public Vector3 Position;
    public Quaternion Rotation;
    public float CurrentHealth;
    public float MaxHealth;
}
