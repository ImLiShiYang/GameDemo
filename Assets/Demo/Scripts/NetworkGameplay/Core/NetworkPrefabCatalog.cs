using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 把服务器下发的稳定 PrefabId 映射为客户端表现对象池。
/// 固定映射：1 为玩家，10/11 为近战/远程敌人，100 为 Boss，200/201 为玩家/敌人子弹。
/// </summary>
public sealed class NetworkPrefabCatalog : MonoBehaviour
{
    public const int PlayerPrefabId = 1;
    public const int TestEnemyPrefabId = 10;
    public const int RangedEnemyPrefabId = 11;
    public const int EnemyProjectilePrefabId = 201;
    public const int BossPrefabId = 100;
    public const int ProjectilePrefabId = 200;

    private readonly Dictionary<int, GameObjectPool> pools = new Dictionary<int, GameObjectPool>();
    private Transform poolRoot;
    private bool initialized;

    public void Initialize()
    {
        if (initialized)
        {
            return;
        }

        initialized = true;
        poolRoot = new GameObject("Network Entity Pool").transform;
        poolRoot.SetParent(transform, false);
        NetworkEnemyPrefabs assets = Resources.Load<NetworkEnemyPrefabs>("NetworkEnemyPrefabs");
        if (assets == null) throw new System.InvalidOperationException("缺少 Resources/NetworkEnemyPrefabs。");
        RegisterPrefab(assets.Melee, TestEnemyPrefabId, 4, 32);
        RegisterPrefab(assets.Ranged, RangedEnemyPrefabId, 4, 32);
        RegisterPrefab(assets.Boss, BossPrefabId, 1, 2);
        RegisterPrefab(assets.Projectile, EnemyProjectilePrefabId, 8, 64);
        RegisterRuntimeProjectile();
    }

    public GameObject Spawn(EntitySpawnMessage message)
    {
        if (!pools.TryGetValue(message.PrefabId, out GameObjectPool pool))
        {
            NetworkLog.Error($"客户端没有配置 PrefabId {message.PrefabId}，EntityId {message.EntityId} 的表现对象创建失败。");
            return null;
        }

        return pool.Get(message.Position, message.Rotation);
    }

    public void Release(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        PooledObject pooledObject = instance.GetComponent<PooledObject>();

        if (pooledObject != null)
        {
            pooledObject.Release();
            return;
        }

        Destroy(instance);
    }

    private void RegisterPrefab(GameObject prefab, int id, int initialSize, int maxSize)
    {
        if (prefab == null) throw new System.InvalidOperationException($"缺少敌人 PrefabId {id} 的预制体。");
        // 在非激活父节点下复制，先关闭单机逻辑，再允许 Unity 激活实例。
        GameObject staging = new GameObject("Inactive staging");
        staging.transform.SetParent(poolRoot, false);
        staging.SetActive(false);
        GameObject template = Instantiate(prefab, staging.transform);
        template.name = $"Network_{prefab.name}_{id}";
        template.SetActive(false);
        foreach (MonoBehaviour component in template.GetComponentsInChildren<MonoBehaviour>(true)) component.enabled = false;
        foreach (Collider component in template.GetComponentsInChildren<Collider>(true)) component.enabled = false;
        foreach (UnityEngine.AI.NavMeshAgent component in template.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) component.enabled = false;
        foreach (Rigidbody body in template.GetComponentsInChildren<Rigidbody>(true)) { body.isKinematic = true; body.detectCollisions = false; }
        foreach (Canvas canvas in template.GetComponentsInChildren<Canvas>(true)) canvas.gameObject.SetActive(false);
        foreach (UnityEngine.Rendering.Universal.DecalProjector decal in template.GetComponentsInChildren<UnityEngine.Rendering.Universal.DecalProjector>(true)) decal.enabled = false;
        foreach (Animator animator in template.GetComponentsInChildren<Animator>(true))
        {
            animator.applyRootMotion = false;
            animator.fireEvents = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        template.transform.SetParent(poolRoot, false);
        if (Application.isPlaying) Destroy(staging);
        else DestroyImmediate(staging);
        Transform storage = new GameObject($"Prefab {id} - {prefab.name}").transform;
        storage.SetParent(poolRoot, false);
        pools.Add(id, new GameObjectPool(template, storage, initialSize, maxSize, instance =>
        {
            foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true)) { animator.Rebind(); animator.Update(0f); }
            foreach (ParticleSystem particles in instance.GetComponentsInChildren<ParticleSystem>(true)) { particles.Clear(); particles.Play(); }
        }));
    }

    private void RegisterRuntimeProjectile()
    {
        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        template.name = "NetworkProjectile_Prefab200";
        template.transform.SetParent(poolRoot, false);
        template.transform.localScale = Vector3.one * 0.22f;

        Renderer visual = template.GetComponent<Renderer>();

        if (visual != null)
        {
            visual.sharedMaterial = new Material(visual.sharedMaterial);
            visual.sharedMaterial.color = new Color(1f, 0.65f, 0.08f, 1f);
        }

        Collider collider = template.GetComponent<Collider>();

        if (collider != null)
        {
            collider.enabled = false;
        }

        template.SetActive(false);
        Transform storageRoot = new GameObject("Prefab 200 - Projectile").transform;
        storageRoot.SetParent(poolRoot, false);
        pools.Add(ProjectilePrefabId, new GameObjectPool(template, storageRoot, 8, 64));
    }

}
