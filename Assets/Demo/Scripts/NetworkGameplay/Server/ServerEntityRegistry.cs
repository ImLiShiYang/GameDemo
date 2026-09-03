using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 服务器通用网络实体注册表。服务器唯一负责 EntityId 分配、Spawn、Despawn 和快照状态。
/// </summary>
public sealed class ServerEntityRegistry : MonoBehaviour
{
    private const int FirstDynamicEntityId = 2001;
    private const int TestRespawnDelayTicks = NetworkRuntime.DefaultTickRate * 3;
    private const float EnemyMoveSpeed = 1.6f;
    private const float EnemyStoppingDistance = 1.8f;
    private const float EnemyHitRadius = 1.1f;

    private readonly Dictionary<int, ServerEntityRecord> entities = new Dictionary<int, ServerEntityRecord>();

    private GameNetworkServer server;
    private ServerPlayerManager playerManager;
    private int nextEntityId = FirstDynamicEntityId;
    private bool scenePrepared;
    private Vector3 testOrigin;
    private ServerEntityRecord testEnemy;
    private uint testRespawnTick;

    public int EntityCount => entities.Count;

    public void Initialize(GameNetworkServer networkServer)
    {
        server = networkServer;
        server.PlayerAuthenticated += HandlePlayerAuthenticated;
        server.ServerTicked += HandleServerTick;
    }

    public void SetPlayerManager(ServerPlayerManager serverPlayerManager)
    {
        playerManager = serverPlayerManager;
    }

    public void PrepareScene()
    {
        if (scenePrepared)
        {
            return;
        }

        scenePrepared = true;
        GrayboxPlayerController playerTemplate = FindObjectOfType<GrayboxPlayerController>(true);
        testOrigin = playerTemplate != null ? playerTemplate.transform.position + Vector3.forward * 4f : Vector3.forward * 4f;
        SpawnTestEnemy();
    }

    public int Register(GameObject gameObject, NetworkEntityType entityType, int prefabId, int ownerPlayerId,
        float currentHealth, float maxHealth)
    {
        int entityId = AllocateEntityId();

        if (gameObject == null)
        {
            NetworkLog.Error($"服务器不能注册空 GameObject，预分配 EntityId {entityId} 已跳过。");
            return 0;
        }

        if (entities.ContainsKey(entityId))
        {
            NetworkLog.Error($"服务器检测到重复 EntityId {entityId}，实体 {gameObject.name} 未注册。");
            return 0;
        }

        NetworkEntity networkEntity = gameObject.GetComponent<NetworkEntity>() ?? gameObject.AddComponent<NetworkEntity>();
        networkEntity.Configure(entityId, entityType, prefabId, ownerPlayerId, true);
        ServerEntityRecord record = new ServerEntityRecord(networkEntity, currentHealth, maxHealth);
        entities.Add(entityId, record);
        server.BroadcastEntitySpawn(CreateSpawnMessage(record));
        NetworkLog.Info($"服务器注册网络实体：EntityId {entityId}，Type {entityType}，PrefabId {prefabId}。");
        return entityId;
    }

    /// <summary>
    /// 根据服务器保存的玩家位置和朝向执行一次权威射线命中。客户端没有传入伤害值或目标的机会。
    /// </summary>
    public bool TryApplyPlayerFire(int sourceEntityId, Vector3 origin, Vector3 direction, float range, float damage)
    {
        if (sourceEntityId <= 0 || direction.sqrMagnitude < 0.0001f || range <= 0f || damage <= 0f)
        {
            return false;
        }

        direction.Normalize();
        ServerEntityRecord closestTarget = null;
        float closestDistance = float.PositiveInfinity;

        foreach (ServerEntityRecord record in entities.Values)
        {
            if (record.Entity == null || record.CurrentHealth <= 0f ||
                (record.Entity.EntityType != NetworkEntityType.Enemy && record.Entity.EntityType != NetworkEntityType.Boss))
            {
                continue;
            }

            Vector3 targetPoint = record.Entity.transform.position + Vector3.up;
            Vector3 toTarget = targetPoint - origin;
            float distanceAlongRay = Vector3.Dot(toTarget, direction);

            if (distanceAlongRay < 0f || distanceAlongRay > range)
            {
                continue;
            }

            float perpendicularDistanceSquared = (toTarget - direction * distanceAlongRay).sqrMagnitude;

            if (perpendicularDistanceSquared > EnemyHitRadius * EnemyHitRadius || distanceAlongRay >= closestDistance)
            {
                continue;
            }

            closestTarget = record;
            closestDistance = distanceAlongRay;
        }

        if (closestTarget == null)
        {
            NetworkLog.Info($"服务器判定 Entity {sourceEntityId} 射击未命中。");
            return false;
        }

        ApplyAuthoritativeDamage(sourceEntityId, closestTarget, damage);
        return true;
    }

    public bool Despawn(int entityId, EntityDespawnReason reason)
    {
        if (!entities.TryGetValue(entityId, out ServerEntityRecord record))
        {
            NetworkLog.Warning($"服务器尝试删除未知 EntityId {entityId}。");
            return false;
        }

        entities.Remove(entityId);
        server.BroadcastEntityDespawn(new EntityDespawnMessage { EntityId = entityId, Reason = reason });

        if (record.Entity != null)
        {
            Destroy(record.Entity.gameObject);
        }

        NetworkLog.Info($"服务器删除网络实体：EntityId {entityId}，Reason {reason}。");
        return true;
    }

    public void AppendSnapshot(WorldSnapshotMessage snapshot)
    {
        foreach (ServerEntityRecord record in entities.Values)
        {
            if (record.Entity == null)
            {
                continue;
            }

            Transform entityTransform = record.Entity.transform;
            snapshot.Entities.Add(new EntityNetworkState
            {
                EntityId = record.Entity.EntityId,
                EntityType = record.Entity.EntityType,
                PrefabId = record.Entity.PrefabId,
                OwnerPlayerId = record.Entity.OwnerPlayerId,
                Position = entityTransform.position,
                RotationY = entityTransform.eulerAngles.y,
                CurrentHealth = record.CurrentHealth,
                MaxHealth = record.MaxHealth,
                AnimationState = record.AnimationState,
                TargetEntityId = record.TargetEntityId
            });
        }
    }

    private void OnDestroy()
    {
        if (server == null)
        {
            return;
        }

        server.PlayerAuthenticated -= HandlePlayerAuthenticated;
        server.ServerTicked -= HandleServerTick;
    }

    private int AllocateEntityId()
    {
        while (entities.ContainsKey(nextEntityId))
        {
            nextEntityId++;
        }

        return nextEntityId++;
    }

    private void HandlePlayerAuthenticated(int playerId, int playerEntityId)
    {
        foreach (ServerEntityRecord record in entities.Values)
        {
            server.SendEntitySpawn(playerId, CreateSpawnMessage(record));
        }
    }

    private void HandleServerTick(uint serverTick, float tickDeltaTime)
    {
        if (!scenePrepared)
        {
            return;
        }

        if (testEnemy != null && testEnemy.Entity != null)
        {
            UpdateEnemyAI(testEnemy, tickDeltaTime);
        }

        if (testEnemy != null || server.ConnectedPlayerCount == 0 || serverTick < testRespawnTick)
        {
            return;
        }

        SpawnTestEnemy();
    }

    private void SpawnTestEnemy()
    {
        GameObject testObject = new GameObject("ServerTestEnemy");
        testObject.transform.SetPositionAndRotation(testOrigin, Quaternion.identity);
        int entityId = Register(testObject, NetworkEntityType.Enemy, NetworkPrefabCatalog.TestEnemyPrefabId, 0, 100f, 100f);

        if (entityId == 0)
        {
            Destroy(testObject);
            return;
        }

        testEnemy = entities[entityId];
    }

    private void UpdateEnemyAI(ServerEntityRecord enemy, float tickDeltaTime)
    {
        if (playerManager == null || !playerManager.TryGetClosestAlivePlayer(enemy.Entity.transform.position,
                out Transform target, out int targetEntityId))
        {
            enemy.AnimationState = 0;
            enemy.TargetEntityId = 0;
            return;
        }

        enemy.TargetEntityId = targetEntityId;
        Vector3 direction = target.position - enemy.Entity.transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude > 0.0001f)
        {
            enemy.Entity.transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
        }

        if (direction.magnitude <= EnemyStoppingDistance)
        {
            enemy.AnimationState = 0;
            return;
        }

        enemy.Entity.transform.position += direction.normalized * (EnemyMoveSpeed * tickDeltaTime);
        enemy.AnimationState = 1;
    }

    private void ApplyAuthoritativeDamage(int sourceEntityId, ServerEntityRecord target, float requestedDamage)
    {
        float damage = Mathf.Min(Mathf.Max(0f, requestedDamage), target.CurrentHealth);

        if (damage <= 0f)
        {
            return;
        }

        target.CurrentHealth -= damage;
        Vector3 position = target.Entity.transform.position;
        server.BroadcastBattleEvent(new BattleEventMessage
        {
            EventType = BattleEventType.Damage,
            SourceEntityId = sourceEntityId,
            TargetEntityId = target.Entity.EntityId,
            Amount = damage,
            CurrentHealth = target.CurrentHealth,
            MaxHealth = target.MaxHealth,
            Position = position
        });
        NetworkLog.Info($"服务器伤害判定：Entity {sourceEntityId} -> {target.Entity.EntityId}，Damage {damage}，HP {target.CurrentHealth}/{target.MaxHealth}。");

        if (target.CurrentHealth > 0f)
        {
            target.AnimationState = 2;
            return;
        }

        int deadEntityId = target.Entity.EntityId;
        server.BroadcastBattleEvent(new BattleEventMessage
        {
            EventType = BattleEventType.EntityDied,
            SourceEntityId = sourceEntityId,
            TargetEntityId = deadEntityId,
            Amount = 0f,
            CurrentHealth = 0f,
            MaxHealth = target.MaxHealth,
            Position = position
        });

        if (ReferenceEquals(testEnemy, target))
        {
            testEnemy = null;
            testRespawnTick = server.ServerTick + TestRespawnDelayTicks;
        }

        Despawn(deadEntityId, EntityDespawnReason.Dead);
    }

    private static EntitySpawnMessage CreateSpawnMessage(ServerEntityRecord record)
    {
        Transform entityTransform = record.Entity.transform;
        return new EntitySpawnMessage
        {
            EntityId = record.Entity.EntityId,
            EntityType = record.Entity.EntityType,
            PrefabId = record.Entity.PrefabId,
            OwnerPlayerId = record.Entity.OwnerPlayerId,
            Position = entityTransform.position,
            Rotation = entityTransform.rotation,
            CurrentHealth = record.CurrentHealth,
            MaxHealth = record.MaxHealth
        };
    }

    private sealed class ServerEntityRecord
    {
        public ServerEntityRecord(NetworkEntity entity, float currentHealth, float maxHealth)
        {
            Entity = entity;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
        }

        public NetworkEntity Entity { get; }
        public float CurrentHealth { get; set; }
        public float MaxHealth { get; }
        public byte AnimationState;
        public int TargetEntityId;
    }
}
