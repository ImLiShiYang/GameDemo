using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 把服务器下发的稳定 PrefabId 映射为客户端表现对象池。
/// 固定映射：1 为玩家，10 为普通敌人，100 为 Boss，200 为客户端纯表现子弹。
/// </summary>
public sealed class NetworkPrefabCatalog : MonoBehaviour
{
    public const int PlayerPrefabId = 1;
    public const int TestEnemyPrefabId = 10;
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
        RegisterRuntimeTestEnemy();
        RegisterRuntimeBoss();
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

    private void RegisterRuntimeTestEnemy()
    {
        GameObject template = CreateCharacterTemplate("NetworkTestEnemy_Prefab10", TestEnemyPrefabId, new Vector3(0.9f, 1.2f, 0.9f));
        template.transform.SetParent(poolRoot, false);

        Renderer visual = template.GetComponentInChildren<Renderer>();

        if (visual != null)
        {
            visual.material.color = new Color(0.85f, 0.12f, 0.12f, 1f);
        }

        Collider collider = template.GetComponent<Collider>();

        if (collider != null)
        {
            collider.enabled = false;
        }

        template.SetActive(false);
        Transform storageRoot = new GameObject("Prefab 10 - Test Enemy").transform;
        storageRoot.SetParent(poolRoot, false);
        pools.Add(TestEnemyPrefabId, new GameObjectPool(template, storageRoot, 2, 16));
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
            visual.material.color = new Color(1f, 0.65f, 0.08f, 1f);
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

    private void RegisterRuntimeBoss()
    {
        GameObject template = CreateCharacterTemplate("NetworkBoss_Prefab100", BossPrefabId, new Vector3(1.8f, 2.2f, 1.8f));
        template.transform.SetParent(poolRoot, false);

        Renderer visual = template.GetComponentInChildren<Renderer>();

        if (visual != null)
        {
            visual.material.color = new Color(0.42f, 0.08f, 0.72f, 1f);
        }

        Collider collider = template.GetComponent<Collider>();

        if (collider != null)
        {
            collider.enabled = false;
        }

        template.SetActive(false);
        Transform storageRoot = new GameObject("Prefab 100 - Boss").transform;
        storageRoot.SetParent(poolRoot, false);
        pools.Add(BossPrefabId, new GameObjectPool(template, storageRoot, 1, 2));
    }

    private static GameObject CreateCharacterTemplate(string name, int prefabId, Vector3 scale)
    {
        GameObject root = new GameObject(name);
        GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        visual.name = "Visual";
        visual.transform.SetParent(root.transform, false);
        visual.transform.localPosition = Vector3.up * (NetworkCharacterShape.ForPrefab(prefabId).Height * 0.5f);
        visual.transform.localScale = scale;
        visual.GetComponent<Collider>().enabled = false;
        return root;
    }
}
