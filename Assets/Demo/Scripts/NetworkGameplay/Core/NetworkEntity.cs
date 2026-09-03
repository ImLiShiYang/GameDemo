using UnityEngine;

public sealed class NetworkEntity : MonoBehaviour
{
    [SerializeField] private int entityId;
    [SerializeField] private NetworkEntityType entityType;
    [SerializeField] private int prefabId;
    [SerializeField] private int ownerPlayerId;
    [SerializeField] private bool isServerAuthority;

    public int EntityId => entityId;
    public NetworkEntityType EntityType => entityType;
    public int PrefabId => prefabId;
    public int OwnerPlayerId => ownerPlayerId;
    public bool IsServerAuthority => isServerAuthority;

    public void Configure(int id, NetworkEntityType type, int prefab, int owner, bool serverAuthority)
    {
        entityId = id;
        entityType = type;
        prefabId = prefab;
        ownerPlayerId = owner;
        isServerAuthority = serverAuthority;
    }
}
