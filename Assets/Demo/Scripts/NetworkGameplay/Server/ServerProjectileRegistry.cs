using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 服务器权威子弹模拟器。服务器创建、移动、碰撞、判伤并删除子弹；客户端只能显示结果。
/// </summary>
public sealed class ServerProjectileRegistry : MonoBehaviour
{
    private const float ProjectileRadius = 0.12f;

    private readonly Dictionary<int, ServerProjectile> projectiles = new Dictionary<int, ServerProjectile>();
    private readonly List<PendingDespawn> pendingDespawns = new List<PendingDespawn>();
    private readonly RaycastHit[] worldHits = new RaycastHit[16];

    private GameNetworkServer server;
    private ServerEntityRegistry entityRegistry;

    public int ProjectileCount => projectiles.Count;

    public void Initialize(GameNetworkServer networkServer, ServerEntityRegistry serverEntityRegistry)
    {
        server = networkServer;
        entityRegistry = serverEntityRegistry;
    }

    public int SpawnPlayerProjectile(int ownerPlayerId, int ownerEntityId, Vector3 origin, Vector3 direction,
        float damage = 10f, int pierceCount = 0, float speed = 15f, float lifetime = 3f)
    {
        if (direction.sqrMagnitude < 0.0001f)
        {
            return 0;
        }

        direction.Normalize();
        Vector3 velocity = direction * Mathf.Max(0.1f, speed);
        GameObject projectileObject = new GameObject($"ServerProjectile_Player{ownerPlayerId}");
        projectileObject.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction, Vector3.up));
        int entityId = entityRegistry.Register(projectileObject, NetworkEntityType.Projectile,
            NetworkPrefabCatalog.ProjectilePrefabId, ownerPlayerId, 0f, 0f, velocity, server.ServerTick);

        if (entityId == 0)
        {
            Destroy(projectileObject);
            return 0;
        }

        projectiles.Add(entityId, new ServerProjectile
        {
            EntityId = entityId,
            SourceEntityId = ownerEntityId,
            GameObject = projectileObject,
            Velocity = velocity,
            ExpireTick = server.ServerTick + (uint)Mathf.Max(1, Mathf.CeilToInt(lifetime * NetworkRuntime.DefaultTickRate)),
            Damage = Mathf.Max(0f, damage),
            RemainingPierces = Mathf.Clamp(pierceCount, 0, 32)
        });
        NetworkLog.Info($"服务器生成权威子弹：EntityId {entityId}，Owner Player {ownerPlayerId}，Velocity {velocity}。");
        return entityId;
    }

    public void SimulateTick(uint serverTick, float tickDeltaTime)
    {
        pendingDespawns.Clear();

        foreach (ServerProjectile projectile in projectiles.Values)
        {
            if (projectile.GameObject == null)
            {
                pendingDespawns.Add(new PendingDespawn(projectile.EntityId, EntityDespawnReason.ProjectileExpired));
                continue;
            }

            if (serverTick >= projectile.ExpireTick)
            {
                pendingDespawns.Add(new PendingDespawn(projectile.EntityId, EntityDespawnReason.ProjectileExpired));
                continue;
            }

            Vector3 start = projectile.GameObject.transform.position;
            Vector3 end = start + projectile.Velocity * tickDeltaTime;
            bool worldHit = TryFindWorldHit(start, end, out Vector3 worldHitPoint, out float worldHitDistance);
            bool consumed = false;
            while (entityRegistry.TryFindProjectileTarget(start, end, ProjectileRadius, projectile.SourceEntityId,
                out int targetEntityId, out Vector3 entityHitPoint, out float entityHitDistance, projectile.HitTargets))
            {
                if (worldHit && worldHitDistance < entityHitDistance) break;
                projectile.HitTargets.Add(targetEntityId);
                entityRegistry.ApplyProjectileDamage(projectile.SourceEntityId, targetEntityId, projectile.Damage);
                if (projectile.RemainingPierces-- <= 0)
                {
                    projectile.GameObject.transform.position = entityHitPoint;
                    consumed = true;
                    break;
                }
            }
            if (worldHit || consumed)
            {
                if (!consumed) projectile.GameObject.transform.position = worldHitPoint;
                pendingDespawns.Add(new PendingDespawn(projectile.EntityId, EntityDespawnReason.ProjectileHit));
                continue;
            }

            projectile.GameObject.transform.position = end;
            entityRegistry.SetEntityVelocity(projectile.EntityId, projectile.Velocity);
        }

        foreach (PendingDespawn pending in pendingDespawns)
        {
            projectiles.Remove(pending.EntityId);
            entityRegistry.Despawn(pending.EntityId, pending.Reason);
        }
    }

    private bool TryFindWorldHit(Vector3 start, Vector3 end, out Vector3 hitPoint, out float hitDistance)
    {
        hitPoint = end;
        hitDistance = float.PositiveInfinity;
        Vector3 delta = end - start;
        float distance = delta.magnitude;

        if (distance < 0.0001f)
        {
            return false;
        }

        int hitCount = Physics.SphereCastNonAlloc(start, ProjectileRadius, delta / distance, worldHits, distance,
            Physics.AllLayers, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = worldHits[i];

            if (hit.collider == null || NetworkCharacterWorld.IsCharacterCollider(hit.collider) || hit.distance >= hitDistance)
            {
                continue;
            }

            hitDistance = hit.distance;
            hitPoint = hit.point;
        }

        return hitDistance < float.PositiveInfinity;
    }

    private sealed class ServerProjectile
    {
        public int EntityId;
        public int SourceEntityId;
        public GameObject GameObject;
        public Vector3 Velocity;
        public uint ExpireTick;
        public float Damage;
        public int RemainingPierces;
        public readonly HashSet<int> HitTargets = new HashSet<int>();
    }

    private readonly struct PendingDespawn
    {
        public PendingDespawn(int entityId, EntityDespawnReason reason)
        {
            EntityId = entityId;
            Reason = reason;
        }

        public int EntityId { get; }
        public EntityDespawnReason Reason { get; }
    }
}
