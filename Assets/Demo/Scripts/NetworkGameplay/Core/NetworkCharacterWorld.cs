using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct CharacterCollisionPose
{
    public readonly int EntityId;
    public readonly int PrefabId;
    public readonly Vector3 Position;
    public CharacterCollisionPose(int entityId, int prefabId, Vector3 position)
    { EntityId = entityId; PrefabId = prefabId; Position = position; }
}

/// <summary>角色碰撞注册表。显示插值不写入此表；重演只临时替换逻辑代理，finally 恢复。</summary>
public sealed class NetworkCharacterWorld : MonoBehaviour
{
    private readonly SortedDictionary<int, NetworkCharacterMotor> bodies = new SortedDictionary<int, NetworkCharacterMotor>();
    private CharacterCollisionPose[] latestContext = Array.Empty<CharacterCollisionPose>();
    private CharacterCollisionPose[] replayContext;
    public CharacterCollisionPose[] LatestContext => latestContext;

    public static NetworkCharacterWorld GetOrCreate(GameObject owner)
        => owner.GetComponent<NetworkCharacterWorld>() ?? owner.AddComponent<NetworkCharacterWorld>();

    public NetworkCharacterMotor Create(int entityId, int prefabId, Vector3 position, bool simulated)
    {
        if (bodies.TryGetValue(entityId, out NetworkCharacterMotor existing)) return existing;
        GameObject body = new GameObject($"NetworkBody_{entityId}");
        body.transform.SetParent(transform, false);
        NetworkCharacterMotor motor = body.AddComponent<NetworkCharacterMotor>();
        motor.Initialize(this, entityId, prefabId, position, simulated);
        bodies.Add(entityId, motor);
        IgnoreControllerPairs(motor);
        return motor;
    }

    public void IgnoreControllerPairs(NetworkCharacterMotor motor)
    {
        if (motor.Controller == null) return;
        foreach (NetworkCharacterMotor other in bodies.Values)
            if (other != motor && other.Controller != null && other.Controller.enabled)
                Physics.IgnoreCollision(motor.Controller, other.Controller, true);
    }

    public void Remove(int entityId)
    {
        if (!bodies.TryGetValue(entityId, out NetworkCharacterMotor motor)) return;
        bodies.Remove(entityId);
        motor.gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(motor.gameObject);
        else DestroyImmediate(motor.gameObject);
    }

    public void SetBlocking(int entityId, bool blocking)
    {
        if (bodies.TryGetValue(entityId, out NetworkCharacterMotor body)) body.SetBlocking(blocking);
        // 已保存的输入上下文保持不可变，新预测不再包含死亡或离场角色。
        RefreshContext();
    }

    public void ApplySnapshot(WorldSnapshotMessage snapshot, int localEntityId)
    {
        HashSet<int> present = new HashSet<int> { localEntityId };
        foreach (PlayerNetworkState player in snapshot.Players)
        {
            present.Add(player.EntityId);
            if (player.EntityId == localEntityId) continue;
            ApplyProxy(player.EntityId, NetworkPrefabCatalog.PlayerPrefabId, player.Position, player.CurrentHealth > 0f);
        }
        foreach (EntityNetworkState entity in snapshot.Entities)
        {
            if (entity.EntityType != NetworkEntityType.Enemy && entity.EntityType != NetworkEntityType.Boss) continue;
            present.Add(entity.EntityId);
            ApplyProxy(entity.EntityId, entity.PrefabId, entity.Position, entity.CurrentHealth > 0f);
        }
        List<int> missing = new List<int>();
        foreach (int id in bodies.Keys) if (!present.Contains(id)) missing.Add(id);
        foreach (int id in missing) Remove(id);
        RefreshContext();
    }

    public void ApplyProxy(int entityId, int prefabId, Vector3 position, bool alive)
    {
        NetworkCharacterMotor proxy = Create(entityId, prefabId, position, false);
        if (proxy.Controller != null) return;
        proxy.Restore(new CharacterMotorState { Position = position });
        proxy.SetBlocking(alive);
    }

    public void RefreshContext()
    {
        List<CharacterCollisionPose> poses = new List<CharacterCollisionPose>();
        foreach (NetworkCharacterMotor body in bodies.Values)
            if (body.Blocking) poses.Add(new CharacterCollisionPose(body.EntityId, body.PrefabId, body.State.Position));
        latestContext = poses.ToArray();
    }

    public IDisposable UseContext(CharacterCollisionPose[] poses)
    {
        if (replayContext != null) throw new InvalidOperationException("不能嵌套角色碰撞重演。");
        replayContext = poses ?? Array.Empty<CharacterCollisionPose>();
        foreach (NetworkCharacterMotor body in bodies.Values)
        {
            if (body.Controller != null) continue;
            body.gameObject.SetActive(false);
            foreach (CharacterCollisionPose pose in replayContext)
            {
                if (pose.EntityId != body.EntityId) continue;
                body.transform.position = pose.Position;
                body.gameObject.SetActive(true);
                break;
            }
        }
        return new ContextScope(this);
    }

    private void EndContext()
    {
        replayContext = null;
        foreach (NetworkCharacterMotor body in bodies.Values)
        {
            if (body.Controller != null) continue;
            body.transform.position = body.State.Position;
            body.gameObject.SetActive(true);
        }
    }

    private sealed class ContextScope : IDisposable
    {
        private NetworkCharacterWorld world;
        public ContextScope(NetworkCharacterWorld owner) { world = owner; }
        public void Dispose() { if (world == null) return; world.EndContext(); world = null; }
    }

    private IEnumerable<CharacterCollisionPose> CollisionPoses()
    {
        if (replayContext != null)
        {
            foreach (CharacterCollisionPose pose in replayContext) yield return pose;
            yield break;
        }
        foreach (NetworkCharacterMotor body in bodies.Values)
            if (body.Blocking) yield return new CharacterCollisionPose(body.EntityId, body.PrefabId, body.State.Position);
    }

    // 竖直胶囊的水平截面：不同楼层的角色不会互相阻挡。
    private static float CombinedRadius(Vector3 position, NetworkCharacterShape shape, CharacterCollisionPose other)
    {
        NetworkCharacterShape otherShape = NetworkCharacterShape.ForPrefab(other.PrefabId);
        float gap = Mathf.Max(0f, Mathf.Max(position.y + shape.Radius - (other.Position.y + otherShape.Height - otherShape.Radius),
            other.Position.y + otherShape.Radius - (position.y + shape.Height - shape.Radius)));
        float radius = shape.Radius + otherShape.Radius;
        return gap >= radius ? 0f : Mathf.Sqrt(radius * radius - gap * gap);
    }

    public bool HasActorOverlap(NetworkCharacterMotor self, Vector3 position)
        => HasActorOverlap(self.EntityId, self.Shape, position);

    private bool HasActorOverlap(int selfId, NetworkCharacterShape shape, Vector3 position)
    {
        foreach (CharacterCollisionPose other in CollisionPoses())
        {
            if (other.EntityId == selfId) continue;
            float radius = CombinedRadius(position, shape, other);
            Vector2 offset = new Vector2(position.x - other.Position.x, position.z - other.Position.z);
            if (radius > 0f && offset.sqrMagnitude < (radius - 0.001f) * (radius - 0.001f)) return true;
        }
        return false;
    }

    public Vector3 ConstrainActors(NetworkCharacterMotor self, Vector3 start, Vector3 displacement)
    {
        Vector3 result = start;
        for (int pass = 0; pass < 3 && displacement.sqrMagnitude > 0.0000001f; pass++)
        {
            float length = displacement.magnitude;
            Vector3 direction = displacement / length;
            float nearest = length;
            Vector3 normal = Vector3.zero;
            foreach (CharacterCollisionPose other in CollisionPoses())
            {
                if (other.EntityId == self.EntityId) continue;
                float radius = CombinedRadius(result, self.Shape, other);
                if (radius <= 0f) continue;
                radius += NetworkCharacterMotor.ContactMargin;
                Vector3 offset = result - other.Position;
                offset.y = 0f;
                float approach = Vector3.Dot(offset, direction);
                // 已有重叠只禁止深入，允许主动退出，不把其他角色挤开。
                if (approach >= 0f) continue;
                float c = offset.sqrMagnitude - radius * radius;
                float discriminant = approach * approach - c;
                if (discriminant < 0f) continue;
                float distance = Mathf.Max(0f, -approach - Mathf.Sqrt(discriminant));
                if (distance > nearest) continue;
                nearest = distance;
                normal = (offset + direction * distance).normalized;
                if (normal.sqrMagnitude < 0.001f) normal = -direction;
            }
            result += direction * nearest;
            if (normal == Vector3.zero) break;
            displacement = Vector3.ProjectOnPlane(direction * (length - nearest), normal);
            displacement.y = 0f;
        }
        return result - start;
    }

    public bool TryFindSpawn(int prefabId, Vector3 requested, out Vector3 position)
    {
        Physics.SyncTransforms();
        NetworkCharacterShape shape = NetworkCharacterShape.ForPrefab(prefabId);
        for (int i = 0; i < 49; i++)
        {
            int ring = (i + 7) / 8;
            float angle = (i - 1) % 8 * 45f * Mathf.Deg2Rad;
            Vector3 candidate = requested + (i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (ring * (shape.Radius * 2f + 0.2f)));
            RaycastHit[] hits = Physics.RaycastAll(candidate + Vector3.up * 3f, Vector3.down, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.normal.y < Mathf.Cos(45f * Mathf.Deg2Rad) || IsCharacterCollider(hit.collider)) continue;
                candidate.y = hit.point.y + shape.Radius * (1f / hit.normal.y - 1f) + NetworkCharacterMotor.ContactMargin;
                bool occupied = HasActorOverlap(0, shape, candidate);
                foreach (Collider collider in Physics.OverlapCapsule(candidate + Vector3.up * shape.Radius,
                    candidate + Vector3.up * (shape.Height - shape.Radius), shape.Radius - 0.01f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    if (!IsCharacterCollider(collider)) { occupied = true; break; }
                if (occupied) break;
                position = candidate;
                return true;
            }
        }
        position = requested;
        return false;
    }

    public static bool IsCharacterCollider(Collider collider)
        => collider != null && (collider.GetComponentInParent<NetworkCharacterMotor>() != null || collider.GetComponentInParent<NetworkEntity>() != null);
}
