using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 对象池总管理器。
/// 对外分为 Bullet / EnemyProjectile / DamageNumber / VFX 四类池。
/// </summary>
public class PoolManager : MonoBehaviour
{
    [Header("Bullet Pool")]
    [SerializeField, Min(0)] private int bulletPrewarmCount = 20;
    [SerializeField, Min(1)] private int bulletMaxSize = 64;

    [Header("EnemyProjectile Pool")]
    [SerializeField, Min(0)] private int enemyProjectilePrewarmCount = 10;
    [SerializeField, Min(1)] private int enemyProjectileMaxSize = 32;

    [Header("DamageNumber Pool")]
    [SerializeField, Min(0)] private int damageNumberPrewarmCount = 20;
    [SerializeField, Min(1)] private int damageNumberMaxSize = 64;

    [Header("VFX Pool")]

    [Header("Enemy Pool")]
    [SerializeField, Min(0)] private int enemyPrewarmCountPerPrefab = 6;
    [SerializeField, Min(1)] private int enemyMaxSizePerPrefab = 24;
    [SerializeField, Min(0)] private int vfxPrewarmCount = 8;
    [SerializeField, Min(1)] private int vfxMaxSizePerPrefab = 32;

    private Transform bulletRoot;
    private Transform enemyProjectileRoot;
    private Transform enemyRoot;
    private Transform damageNumberRoot;
    private Transform vfxRoot;

    private GameObjectPool bulletPool;
    private GameObject bulletPrefabKey;

    private GameObjectPool enemyProjectilePool;
    private GameObject enemyProjectilePrefabKey;

    private GameObjectPool damageNumberPool;
    private GameObject damageNumberPrefabKey;
    private readonly Dictionary<GameObject, GameObjectPool> enemyPools = new();

    private readonly Dictionary<GameObject, GameObjectPool> vfxPools = new();

    private void Awake()
    {
        bulletRoot = CreateCategoryRoot("Bullet Pool");
        enemyProjectileRoot = CreateCategoryRoot("EnemyProjectile Pool");
        enemyRoot = CreateCategoryRoot("Enemy Pool");
        damageNumberRoot = CreateCategoryRoot("DamageNumber Pool");
        vfxRoot = CreateCategoryRoot("VFX Pool");
    }


    public void WarmBulletPool(Projectile prefab)
    {
        if (prefab == null)
        {
            return;
        }

        EnsureSinglePrefabPool(
            ref bulletPool,
            ref bulletPrefabKey,
            prefab.gameObject,
            bulletRoot,
            bulletPrewarmCount,
            bulletMaxSize,
            "Bullet Pool"
        );
    }

    public void WarmEnemyProjectilePool(EnemyHomingProjectile prefab)
    {
        if (prefab == null)
        {
            return;
        }

        EnsureSinglePrefabPool(
            ref enemyProjectilePool,
            ref enemyProjectilePrefabKey,
            prefab.gameObject,
            enemyProjectileRoot,
            enemyProjectilePrewarmCount,
            enemyProjectileMaxSize,
            "EnemyProjectile Pool"
        );
    }

    public void WarmDamageNumberPool(DamageNumber prefab)
    {
        if (prefab == null)
        {
            return;
        }

        EnsureSinglePrefabPool(
            ref damageNumberPool,
            ref damageNumberPrefabKey,
            prefab.gameObject,
            damageNumberRoot,
            damageNumberPrewarmCount,
            damageNumberMaxSize,
            "DamageNumber Pool"
        );
    }

    public void WarmVFXPool(GameObject prefab)
    {
        if (prefab == null)
        {
            return;
        }

        GetOrCreateVfxPool(prefab);
    }

    public void WarmEnemyPool(GameObject prefab)
    {
        if (prefab == null)
        {
            return;
        }

        GetOrCreateEnemyPool(prefab);
    }

    public GameObject GetEnemy(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation)
    {
        if (prefab == null)
        {
            Debug.LogError("GetEnemy received a null prefab.", this);
            return null;
        }

        return GetOrCreateEnemyPool(prefab).Get(position, rotation);
    }

    public Projectile GetBullet(Projectile prefab, Vector3 position,Quaternion rotation)
    {
        if (prefab == null)
        {
            Debug.LogError("GetBullet 收到空 Prefab。", this);
            return null;
        }

        if (!EnsureSinglePrefabPool(
                ref bulletPool,
                ref bulletPrefabKey,
                prefab.gameObject,
                bulletRoot,
                bulletPrewarmCount,
                bulletMaxSize,
                "Bullet Pool"))
        {
            return null;
        }

        return bulletPool
            .Get(position, rotation)
            .GetComponent<Projectile>();
    }

    public EnemyHomingProjectile GetEnemyProjectile(
        EnemyHomingProjectile prefab,
        Vector3 position,
        Quaternion rotation)
    {
        if (prefab == null)
        {
            Debug.LogError("GetEnemyProjectile 收到空 Prefab。", this);
            return null;
        }

        if (!EnsureSinglePrefabPool(
                ref enemyProjectilePool,
                ref enemyProjectilePrefabKey,
                prefab.gameObject,
                enemyProjectileRoot,
                enemyProjectilePrewarmCount,
                enemyProjectileMaxSize,
                "EnemyProjectile Pool"))
        {
            return null;
        }

        return enemyProjectilePool
            .Get(position, rotation)
            .GetComponent<EnemyHomingProjectile>();
    }

    public DamageNumber GetDamageNumber(
        DamageNumber prefab,
        Vector3 position,
        Quaternion rotation)
    {
        if (prefab == null)
        {
            Debug.LogError("GetDamageNumber 收到空 Prefab。", this);
            return null;
        }

        if (!EnsureSinglePrefabPool(
                ref damageNumberPool,
                ref damageNumberPrefabKey,
                prefab.gameObject,
                damageNumberRoot,
                damageNumberPrewarmCount,
                damageNumberMaxSize,
                "DamageNumber Pool"))
        {
            return null;
        }

        return damageNumberPool
            .Get(position, rotation)
            .GetComponent<DamageNumber>();
    }

    /// <summary>
    /// VFX Pool 允许同时管理多个不同的特效 Prefab。
    /// 每个 Prefab 内部拥有自己的 GameObjectPool，但统一收纳在 VFX Pool 节点下。
    /// </summary>
    public GameObject GetVFX(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        float lifeTime,
        Transform parent = null)
    {
        if (prefab == null)
        {
            return null;
        }

        GameObjectPool pool = GetOrCreateVfxPool(prefab);
        GameObject effect = pool.Get(position, rotation, parent);

        PooledObject pooledObject = effect.GetComponent<PooledObject>();

        if (pooledObject != null && lifeTime > 0f)
        {
            int version = pooledObject.Version;
            StartCoroutine(ReleaseAfterDelay(pooledObject, lifeTime, version));
        }

        return effect;
    }

    public void Release(GameObject instance)
    {
        if (instance == null)
        {
            return;
        }

        PooledObject pooledObject = instance.GetComponent<PooledObject>();

        if (pooledObject == null)
        {
            Debug.LogWarning(
                $"{instance.name} 不是由 PoolManager 创建的对象，将直接销毁。",
                instance
            );

            Destroy(instance);
            return;
        }

        pooledObject.Release();
    }


    private GameObjectPool GetOrCreateVfxPool(GameObject prefab)
    {
        if (vfxPools.TryGetValue(prefab, out GameObjectPool existingPool))
        {
            return existingPool;
        }

        Transform prefabPoolRoot = new GameObject(prefab.name).transform;
        prefabPoolRoot.SetParent(vfxRoot, false);

        GameObjectPool newPool = new GameObjectPool(
            prefab,
            prefabPoolRoot,
            vfxPrewarmCount,
            vfxMaxSizePerPrefab,
            RestartVfx,
            StopVfx
        );

        vfxPools.Add(prefab, newPool);
        return newPool;
    }

    private GameObjectPool GetOrCreateEnemyPool(GameObject prefab)
    {
        if (enemyPools.TryGetValue(prefab, out GameObjectPool existingPool))
        {
            return existingPool;
        }

        Transform prefabPoolRoot = new GameObject(prefab.name).transform;
        prefabPoolRoot.SetParent(enemyRoot, false);

        GameObjectPool newPool = new GameObjectPool(
            prefab,
            prefabPoolRoot,
            enemyPrewarmCountPerPrefab,
            enemyMaxSizePerPrefab,
            RestartEnemy,
            StopEnemy
        );

        enemyPools.Add(prefab, newPool);
        return newPool;
    }

    private IEnumerator ReleaseAfterDelay(
        PooledObject pooledObject,
        float delay,
        int version)
    {
        yield return new WaitForSeconds(delay);

        if (pooledObject == null ||pooledObject.IsInPool || pooledObject.Version != version)
        {
            yield break;
        }

        pooledObject.Release();
    }

    private bool EnsureSinglePrefabPool(ref GameObjectPool pool,ref GameObject prefabKey,GameObject requestedPrefab,
        Transform root,int prewarmCount,int maxSize,string poolName)
    {
        if (pool == null)
        {
            prefabKey = requestedPrefab;
            pool = new GameObjectPool(
                requestedPrefab,
                root,
                prewarmCount,
                maxSize
            );

            return true;
        }

        if (prefabKey == requestedPrefab)
        {
            return true;
        }

        Debug.LogError(
            $"{poolName} 当前已经绑定 Prefab '{prefabKey.name}'，" +
            $"不能再传入另一个 Prefab '{requestedPrefab.name}'。",
            this
        );

        return false;
    }

    private Transform CreateCategoryRoot(string rootName)
    {
        Transform existing = transform.Find(rootName);

        if (existing != null)
        {
            return existing;
        }

        GameObject rootObject = new GameObject(rootName);
        Transform rootTransform = rootObject.transform;
        rootTransform.SetParent(transform, false);

        return rootTransform;
    }

    private static void RestartEnemy(GameObject enemy)
    {
        Health health = enemy.GetComponent<Health>();
        health?.ResetForReuse();

        EnemyAnimationController animationController =
            enemy.GetComponent<EnemyAnimationController>();
        animationController?.ResetForReuse();

        EnemyAIController aiController = enemy.GetComponent<EnemyAIController>();
        aiController?.ResetForReuse();
    }

    private static void StopEnemy(GameObject enemy)
    {
        UnityEngine.AI.NavMeshAgent agent =
            enemy.GetComponent<UnityEngine.AI.NavMeshAgent>();

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    private static void RestartVfx(GameObject effect)
    {
        ParticleSystem[] particleSystems =
            effect.GetComponentsInChildren<ParticleSystem>(true);

        foreach (ParticleSystem particleSystem in particleSystems)
        {
            particleSystem.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );

            if (particleSystem.gameObject.activeInHierarchy)
            {
                particleSystem.Play(true);
            }
        }
    }

    private static void StopVfx(GameObject effect)
    {
        ParticleSystem[] particleSystems =
            effect.GetComponentsInChildren<ParticleSystem>(true);

        foreach (ParticleSystem particleSystem in particleSystems)
        {
            particleSystem.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear
            );
        }
    }
}
