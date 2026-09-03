using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class ClientEntityRegistry : MonoBehaviour
{
    private readonly Dictionary<int, NetworkEntity> entities = new Dictionary<int, NetworkEntity>();
    private readonly HashSet<int> snapshotPlayerIds = new HashSet<int>();
    private readonly Dictionary<int, uint> despawnedEntityTicks = new Dictionary<int, uint>();
    private readonly HashSet<int> reportedUnknownEntityIds = new HashSet<int>();

    private GameNetworkClient client;
    private NetworkPrefabCatalog prefabCatalog;
    private GameObject playerTemplate;
    private Transform localPlayerTransform;

    public int EntityCount => entities.Count;
    public Transform LocalPlayerTransform => localPlayerTransform;

    public bool Initialize(GameNetworkClient networkClient)
    {
        client = networkClient;
        prefabCatalog = GetComponent<NetworkPrefabCatalog>() ?? gameObject.AddComponent<NetworkPrefabCatalog>();
        prefabCatalog.Initialize();
        GrayboxPlayerController scenePlayer = FindObjectOfType<GrayboxPlayerController>(true);

        if (scenePlayer == null)
        {
            NetworkLog.Error("客户端主场景没有找到 Player_Graybox，无法创建玩家表现。");
            return false;
        }

        playerTemplate = scenePlayer.gameObject;
        DisableClientGameplay(playerTemplate);
        NetworkEntity localEntity = playerTemplate.GetComponent<NetworkEntity>() ?? playerTemplate.AddComponent<NetworkEntity>();
        localEntity.Configure(NetworkRuntime.LocalPlayerEntityId, NetworkEntityType.Player, NetworkPrefabCatalog.PlayerPrefabId,
            NetworkRuntime.LocalPlayerId, false);
        NetworkTransformInterpolator interpolator = playerTemplate.GetComponent<NetworkTransformInterpolator>() ?? playerTemplate.AddComponent<NetworkTransformInterpolator>();
        interpolator.Initialize(true);
        playerTemplate.name = $"NetworkPlayer_Local_{NetworkRuntime.LocalPlayerId}";
        entities[localEntity.EntityId] = localEntity;
        localPlayerTransform = playerTemplate.transform;
        client.SnapshotReceived += HandleSnapshot;
        client.EntitySpawnReceived += HandleEntitySpawn;
        client.EntityDespawnReceived += HandleEntityDespawn;
        client.BattleEventReceived += HandleBattleEvent;
        client.ReplayKnownEntitySpawns(HandleEntitySpawn);
        NetworkLog.Info($"客户端已绑定本地玩家表现：EntityId {localEntity.EntityId}。");
        return true;
    }

    private void OnDestroy()
    {
        if (client != null)
        {
            client.SnapshotReceived -= HandleSnapshot;
            client.EntitySpawnReceived -= HandleEntitySpawn;
            client.EntityDespawnReceived -= HandleEntityDespawn;
            client.BattleEventReceived -= HandleBattleEvent;
        }
    }

    private void HandleSnapshot(WorldSnapshotMessage snapshot)
    {
        snapshotPlayerIds.Clear();

        foreach (PlayerNetworkState state in snapshot.Players)
        {
            snapshotPlayerIds.Add(state.EntityId);
            NetworkEntity entity = GetOrCreatePlayer(state);

            if (entity == null)
            {
                continue;
            }

            NetworkTransformInterpolator interpolator = entity.GetComponent<NetworkTransformInterpolator>();
            interpolator.ApplyState(state);
        }

        foreach (EntityNetworkState state in snapshot.Entities)
        {
            if (despawnedEntityTicks.ContainsKey(state.EntityId))
            {
                continue;
            }

            if (!entities.TryGetValue(state.EntityId, out NetworkEntity entity))
            {
                if (reportedUnknownEntityIds.Add(state.EntityId))
                {
                    NetworkLog.Warning($"客户端快照引用未知 EntityId {state.EntityId}；等待 EntitySpawn，不从快照擅自创建。");
                }

                continue;
            }

            if (entity.EntityType != state.EntityType || entity.PrefabId != state.PrefabId)
            {
                NetworkLog.Error($"EntityId {state.EntityId} 的快照类型与已创建对象不一致。");
                continue;
            }

            if (state.EntityType == NetworkEntityType.Projectile)
            {
                ClientProjectileView projectileView = entity.GetComponent<ClientProjectileView>();
                projectileView?.ApplyState(state);
            }
            else
            {
                NetworkTransformInterpolator interpolator = entity.GetComponent<NetworkTransformInterpolator>();
                interpolator?.ApplyState(state);
                NetworkEntityHealthView healthView = entity.GetComponent<NetworkEntityHealthView>();
                healthView?.ApplyHealth(state.CurrentHealth, state.MaxHealth);
            }
        }

        RemoveMissingRemotePlayers();
    }

    private NetworkEntity GetOrCreatePlayer(PlayerNetworkState state)
    {
        if (entities.TryGetValue(state.EntityId, out NetworkEntity existing))
        {
            if (existing.EntityType != NetworkEntityType.Player)
            {
                NetworkLog.Error($"重复 EntityId {state.EntityId}：玩家快照与已有 {existing.EntityType} 对象冲突。");
                return null;
            }

            return existing;
        }

        GameObject playerObject = Instantiate(playerTemplate);
        playerObject.name = $"NetworkPlayer_Remote_{state.OwnerPlayerId}";
        DisableClientGameplay(playerObject);
        NetworkEntity entity = playerObject.GetComponent<NetworkEntity>() ?? playerObject.AddComponent<NetworkEntity>();
        entity.Configure(state.EntityId, NetworkEntityType.Player, NetworkPrefabCatalog.PlayerPrefabId, state.OwnerPlayerId, false);
        NetworkTransformInterpolator interpolator = playerObject.GetComponent<NetworkTransformInterpolator>() ?? playerObject.AddComponent<NetworkTransformInterpolator>();
        interpolator.Initialize(false);
        entities.Add(state.EntityId, entity);
        NetworkLog.Info($"客户端创建远程玩家表现：Player {state.OwnerPlayerId}，EntityId {state.EntityId}。");
        return entity;
    }

    private void RemoveMissingRemotePlayers()
    {
        List<int> missingIds = null;

        foreach (KeyValuePair<int, NetworkEntity> pair in entities)
        {
            if (pair.Value.EntityType != NetworkEntityType.Player || pair.Key == NetworkRuntime.LocalPlayerEntityId ||
                snapshotPlayerIds.Contains(pair.Key))
            {
                continue;
            }

            missingIds ??= new List<int>();
            missingIds.Add(pair.Key);
        }

        if (missingIds == null)
        {
            return;
        }

        foreach (int entityId in missingIds)
        {
            NetworkEntity entity = entities[entityId];
            entities.Remove(entityId);
            Destroy(entity.gameObject);
            NetworkLog.Info($"客户端移除已断开的远程玩家 EntityId {entityId}。");
        }
    }

    private void HandleEntitySpawn(EntitySpawnMessage message, uint serverTick)
    {
        if (message.EntityType == NetworkEntityType.Player)
        {
            NetworkLog.Warning($"客户端忽略 Player 类型的通用 EntitySpawn {message.EntityId}；玩家由 PlayerState 管理。");
            return;
        }

        if (despawnedEntityTicks.ContainsKey(message.EntityId))
        {
            NetworkLog.Warning($"客户端忽略已删除 EntityId {message.EntityId} 的旧 Spawn。");
            return;
        }

        if (entities.TryGetValue(message.EntityId, out NetworkEntity existing))
        {
            if (existing.EntityType == message.EntityType && existing.PrefabId == message.PrefabId)
            {
                NetworkLog.Warning($"客户端过滤重复 EntitySpawn：EntityId {message.EntityId}。");
            }
            else
            {
                NetworkLog.Error($"客户端检测到重复 EntityId {message.EntityId}，已有类型 {existing.EntityType}，新类型 {message.EntityType}。");
            }

            return;
        }

        GameObject entityObject = prefabCatalog.Spawn(message);

        if (entityObject == null)
        {
            return;
        }

        entityObject.name = $"NetworkEntity_{message.EntityType}_{message.EntityId}";
        reportedUnknownEntityIds.Remove(message.EntityId);
        DisableClientGameplay(entityObject);
        NetworkEntity entity = entityObject.GetComponent<NetworkEntity>() ?? entityObject.AddComponent<NetworkEntity>();
        entity.Configure(message.EntityId, message.EntityType, message.PrefabId, message.OwnerPlayerId, false);

        if (message.EntityType == NetworkEntityType.Projectile)
        {
            ClientProjectileView projectileView = entityObject.GetComponent<ClientProjectileView>() ??
                entityObject.AddComponent<ClientProjectileView>();
            projectileView.enabled = true;
            projectileView.Initialize(message, serverTick);
        }
        else
        {
            NetworkTransformInterpolator interpolator = entityObject.GetComponent<NetworkTransformInterpolator>() ??
                entityObject.AddComponent<NetworkTransformInterpolator>();
            interpolator.enabled = true;
            interpolator.Initialize(false);
            interpolator.ApplySpawn(message);
            NetworkEntityHealthView healthView = entityObject.GetComponent<NetworkEntityHealthView>() ??
                entityObject.AddComponent<NetworkEntityHealthView>();
            healthView.enabled = true;
            healthView.Initialize(entity, message.CurrentHealth, message.MaxHealth);
        }
        entities.Add(message.EntityId, entity);
        NetworkLog.Info($"客户端从对象池创建实体：EntityId {message.EntityId}，Type {message.EntityType}，PrefabId {message.PrefabId}。");
    }

    private void HandleEntityDespawn(EntityDespawnMessage message, uint serverTick)
    {
        if (despawnedEntityTicks.TryGetValue(message.EntityId, out uint previousTick) && previousTick >= serverTick)
        {
            NetworkLog.Warning($"客户端过滤重复 EntityDespawn：EntityId {message.EntityId}。");
            return;
        }

        despawnedEntityTicks[message.EntityId] = serverTick;
        reportedUnknownEntityIds.Remove(message.EntityId);

        if (!entities.TryGetValue(message.EntityId, out NetworkEntity entity))
        {
            NetworkLog.Warning($"客户端收到未知 EntityId {message.EntityId} 的 Despawn。");
            return;
        }

        if (entity.EntityType == NetworkEntityType.Player)
        {
            NetworkLog.Error($"通用 EntityDespawn 不能删除玩家 EntityId {message.EntityId}。");
            return;
        }

        entities.Remove(message.EntityId);
        NetworkEntityHealthView healthView = entity.GetComponent<NetworkEntityHealthView>();

        if (message.Reason == EntityDespawnReason.Dead)
        {
            healthView?.PlayDeath();
            StartCoroutine(ReleaseAfterDeath(entity.gameObject));
        }
        else
        {
            prefabCatalog.Release(entity.gameObject);
        }

        NetworkLog.Info($"客户端回收实体：EntityId {message.EntityId}，Reason {message.Reason}。");
    }

    private void HandleBattleEvent(BattleEventMessage message, uint serverTick)
    {
        if (message.EventType == BattleEventType.PlayerFired)
        {
            if (entities.TryGetValue(message.SourceEntityId, out NetworkEntity sourceEntity))
            {
                sourceEntity.GetComponent<NetworkTransformInterpolator>()?.PlayAttack();
            }
            else
            {
                NetworkLog.Warning($"客户端开火事件引用未知 SourceEntityId {message.SourceEntityId}。");
            }

            return;
        }

        if (message.EventType != BattleEventType.Damage && message.EventType != BattleEventType.EntityDied)
        {
            return;
        }

        if (!entities.TryGetValue(message.TargetEntityId, out NetworkEntity entity))
        {
            if (!despawnedEntityTicks.ContainsKey(message.TargetEntityId))
            {
                NetworkLog.Warning($"客户端战斗事件引用未知 EntityId {message.TargetEntityId}。");
            }

            return;
        }

        NetworkEntityHealthView healthView = entity.GetComponent<NetworkEntityHealthView>();

        if (healthView == null)
        {
            return;
        }

        healthView.ApplyHealth(message.CurrentHealth, message.MaxHealth);

        if (message.EventType == BattleEventType.Damage)
        {
            healthView.PlayDamage(message.Amount);
            NetworkLog.Info($"客户端表现伤害：{message.SourceEntityId} -> {message.TargetEntityId}，{message.Amount}。");
        }
        else if (message.EventType == BattleEventType.EntityDied)
        {
            healthView.PlayDeath();
            NetworkLog.Info($"客户端表现敌人死亡：EntityId {message.TargetEntityId}。");
        }
    }

    private IEnumerator ReleaseAfterDeath(GameObject entityObject)
    {
        yield return new WaitForSecondsRealtime(0.35f);
        prefabCatalog.Release(entityObject);
    }

    private static void DisableClientGameplay(GameObject playerObject)
    {
        foreach (MonoBehaviour behaviour in playerObject.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour is NetworkEntity || behaviour is NetworkTransformInterpolator ||
                behaviour is NetworkEntityHealthView || behaviour is ClientProjectileView)
            {
                continue;
            }

            behaviour.enabled = false;
        }

        CharacterController characterController = playerObject.GetComponent<CharacterController>();

        if (characterController != null)
        {
            characterController.enabled = false;
        }

        UnityEngine.AI.NavMeshAgent navMeshAgent = playerObject.GetComponent<UnityEngine.AI.NavMeshAgent>();

        if (navMeshAgent != null)
        {
            navMeshAgent.enabled = false;
        }

        foreach (LineRenderer lineRenderer in playerObject.GetComponentsInChildren<LineRenderer>(true))
        {
            lineRenderer.enabled = false;
        }

        foreach (AudioSource audioSource in playerObject.GetComponentsInChildren<AudioSource>(true))
        {
            audioSource.enabled = false;
        }
    }
}
