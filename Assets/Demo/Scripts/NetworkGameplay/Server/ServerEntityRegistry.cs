using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 服务器通用网络实体注册表。服务器唯一负责 EntityId 分配、Spawn、Despawn 和快照状态。
/// </summary>
public sealed class ServerEntityRegistry : MonoBehaviour
{
    private const int FirstDynamicEntityId = 2001;
    private const float EnemyMoveSpeed = 1.6f;
    private const float EnemyStoppingDistance = 1.8f;
    private const float BossMoveSpeed = 1.15f;
    private const float BossStoppingDistance = 2.4f;
    private const float EnemyHitRadius = 1.1f;

    private readonly Dictionary<int, ServerEntityRecord> entities = new Dictionary<int, ServerEntityRecord>();

    private GameNetworkServer server;
    private ServerPlayerManager playerManager;
    private int nextEntityId = FirstDynamicEntityId;
    private bool scenePrepared;

    public int EntityCount => entities.Count;
    public event Action<int, NetworkEntityType, Vector3> EntityDied;

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
        NetworkLog.Info("服务器网络实体注册表已准备，实体将由 ServerBattleFlow 创建。");
    }

    public int Register(GameObject gameObject, NetworkEntityType entityType, int prefabId, int ownerPlayerId,
        float currentHealth, float maxHealth, Vector3 velocity = default, uint spawnTick = 0)
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
        ServerEntityRecord record = new ServerEntityRecord(networkEntity, currentHealth, maxHealth, velocity, spawnTick);
        entities.Add(entityId, record);
        server.BroadcastEntitySpawn(CreateSpawnMessage(record));
        NetworkLog.Info($"服务器注册网络实体：EntityId {entityId}，Type {entityType}，PrefabId {prefabId}。");
        return entityId;
    }

    public bool TryFindProjectileTarget(Vector3 start, Vector3 end, float projectileRadius, int ownerEntityId,
        out int targetEntityId, out Vector3 hitPoint, out float hitDistance)
    {
        targetEntityId = 0;
        hitPoint = end;
        hitDistance = float.PositiveInfinity;
        Vector3 segment = end - start;
        float segmentLengthSquared = segment.sqrMagnitude;

        if (segmentLengthSquared < 0.000001f)
        {
            return false;
        }

        float segmentLength = Mathf.Sqrt(segmentLengthSquared);
        float combinedRadius = EnemyHitRadius + Mathf.Max(0f, projectileRadius);
        float combinedRadiusSquared = combinedRadius * combinedRadius;

        foreach (ServerEntityRecord record in entities.Values)
        {
            if (record.Entity == null || record.Entity.EntityId == ownerEntityId || record.CurrentHealth <= 0f ||
                (record.Entity.EntityType != NetworkEntityType.Enemy && record.Entity.EntityType != NetworkEntityType.Boss))
            {
                continue;
            }

            Vector3 targetCenter = record.Entity.transform.position + Vector3.up;
            float segmentFraction = Mathf.Clamp01(Vector3.Dot(targetCenter - start, segment) / segmentLengthSquared);
            Vector3 closestPoint = start + segment * segmentFraction;

            if ((targetCenter - closestPoint).sqrMagnitude > combinedRadiusSquared)
            {
                continue;
            }

            float distance = segmentFraction * segmentLength;

            if (distance >= hitDistance)
            {
                continue;
            }

            targetEntityId = record.Entity.EntityId;
            hitPoint = closestPoint;
            hitDistance = distance;
        }

        return targetEntityId > 0;
    }

    public bool ApplyProjectileDamage(int sourceEntityId, int targetEntityId, float damage)
    {
        if (!entities.TryGetValue(targetEntityId, out ServerEntityRecord target) || target.CurrentHealth <= 0f)
        {
            return false;
        }

        ApplyAuthoritativeDamage(sourceEntityId, target, damage);
        return true;
    }

    public void SetEntityVelocity(int entityId, Vector3 velocity)
    {
        if (entities.TryGetValue(entityId, out ServerEntityRecord record))
        {
            record.Velocity = velocity;
        }
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
                Velocity = record.Velocity,
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

        foreach (ServerEntityRecord record in entities.Values)
        {
            if (record.Entity != null && (record.Entity.EntityType == NetworkEntityType.Enemy ||
                record.Entity.EntityType == NetworkEntityType.Boss))
            {
                UpdateEnemyAI(record, tickDeltaTime);
            }
        }
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

        bool isBoss = enemy.Entity.EntityType == NetworkEntityType.Boss;
        float stoppingDistance = isBoss ? BossStoppingDistance : EnemyStoppingDistance;

        if (direction.magnitude <= stoppingDistance)
        {
            enemy.AnimationState = 0;
            return;
        }

        float moveSpeed = isBoss ? BossMoveSpeed : EnemyMoveSpeed;
        enemy.Entity.transform.position += direction.normalized * (moveSpeed * tickDeltaTime);
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
        NetworkEntityType deadEntityType = target.Entity.EntityType;
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

        EntityDied?.Invoke(deadEntityId, deadEntityType, position);
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
            Velocity = record.Velocity,
            SpawnTick = record.SpawnTick,
            CurrentHealth = record.CurrentHealth,
            MaxHealth = record.MaxHealth
        };
    }

    private sealed class ServerEntityRecord
    {
        public ServerEntityRecord(NetworkEntity entity, float currentHealth, float maxHealth, Vector3 velocity, uint spawnTick)
        {
            Entity = entity;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            Velocity = velocity;
            SpawnTick = spawnTick;
        }

        public NetworkEntity Entity { get; }
        public float CurrentHealth { get; set; }
        public float MaxHealth { get; }
        public Vector3 Velocity;
        public uint SpawnTick { get; }
        public byte AnimationState;
        public int TargetEntityId;
    }
}
