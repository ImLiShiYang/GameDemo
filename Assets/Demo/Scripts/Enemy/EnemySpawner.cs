using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemySpawner : MonoBehaviour
{
    [Header("Spawn Points")]
    [SerializeField]
    private Transform[] spawnPoints;

    [Header("References")]
    [SerializeField]
    private Transform player;

    [Header("Spawn Check")]
    [SerializeField, Min(0.1f)]
    private float navMeshSampleDistance = 2f;

    [SerializeField, Min(0f)]
    private float minDistanceFromPlayer = 8f;

    [SerializeField, Min(0f)]
    private float minEnemySpacing = 1.5f;

    private readonly List<GameObject> spawnedEnemies = new();

    private PoolManager poolManager;

    private void Start()
    {
        ResolveReferences();
    }

    /// <summary>
    /// WaveManager 调用这个函数：
    /// 指定一个敌人 Prefab，让 EnemySpawner 找位置并生成。
    /// </summary>
    public GameObject Spawn(GameObject enemyPrefab)
    {
        if (enemyPrefab == null)
        {
            Debug.LogError(
                "EnemySpawner 收到了空的 Enemy Prefab。",
                this
            );

            return null;
        }

        ResolveReferences();
        CleanupEnemies();

        if (poolManager == null)
        {
            Debug.LogError(
                "EnemySpawner 没有找到 PoolManager。",
                this
            );

            return null;
        }

        if (!TryGetSpawnPoint(
                out Vector3 spawnPosition,
                out Quaternion spawnRotation))
        {
            Debug.LogWarning(
                "EnemySpawner 没有找到合适的出生点。",
                this
            );

            return null;
        }

        GameObject enemy = poolManager.GetEnemy(
            enemyPrefab,
            spawnPosition,
            spawnRotation
        );

        if (enemy != null)
        {
            spawnedEnemies.Add(enemy);
        }

        return enemy;
    }

    /// <summary>
    /// 从所有 SpawnPoint 中随机挑一个合法出生点。
    /// </summary>
    private bool TryGetSpawnPoint(
        out Vector3 spawnPosition,
        out Quaternion spawnRotation)
    {
        spawnPosition = default;
        spawnRotation = Quaternion.identity;

        if (spawnPoints == null ||
            spawnPoints.Length == 0)
        {
            Debug.LogError(
                "EnemySpawner 没有配置 SpawnPoints。",
                this
            );

            return false;
        }

        // 不总是从 SpawnPoint[0] 开始，
        // 而是随机一个起点。
        int startIndex =
            Random.Range(0, spawnPoints.Length);

        for (int offset = 0;
             offset < spawnPoints.Length;
             offset++)
        {
            int index =
                (startIndex + offset) %
                spawnPoints.Length;

            Transform spawnPoint =
                spawnPoints[index];

            if (spawnPoint == null)
            {
                continue;
            }

            // SpawnPoint 附近必须存在 NavMesh。
            if (!NavMesh.SamplePosition(
                    spawnPoint.position,
                    out NavMeshHit hit,
                    navMeshSampleDistance,
                    NavMesh.AllAreas))
            {
                continue;
            }

            if (!IsFarEnoughFromPlayer(
                    hit.position))
            {
                continue;
            }

            if (!IsFarEnoughFromEnemies(
                    hit.position))
            {
                continue;
            }

            spawnPosition = hit.position;

            // 默认让小怪生成时朝向玩家。
            spawnRotation =
                GetSpawnRotation(
                    spawnPoint,
                    spawnPosition
                );

            return true;
        }

        return false;
    }

    private Quaternion GetSpawnRotation(
        Transform spawnPoint,
        Vector3 spawnPosition)
    {
        if (player == null)
        {
            return spawnPoint.rotation;
        }

        Vector3 direction =
            player.position -
            spawnPosition;

        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            return spawnPoint.rotation;
        }

        return Quaternion.LookRotation(
            direction.normalized,
            Vector3.up
        );
    }

    private bool IsFarEnoughFromPlayer(
        Vector3 position)
    {
        if (player == null ||
            minDistanceFromPlayer <= 0f)
        {
            return true;
        }

        Vector3 difference =
            position - player.position;

        difference.y = 0f;

        float minSqrDistance =
            minDistanceFromPlayer *
            minDistanceFromPlayer;

        return difference.sqrMagnitude >=
               minSqrDistance;
    }

    private bool IsFarEnoughFromEnemies(
        Vector3 position)
    {
        if (minEnemySpacing <= 0f)
        {
            return true;
        }

        float minSqrDistance =
            minEnemySpacing *
            minEnemySpacing;

        foreach (GameObject enemy
                 in spawnedEnemies)
        {
            if (enemy == null ||
                !enemy.activeInHierarchy)
            {
                continue;
            }

            Health health =
                enemy.GetComponent<Health>();

            // 已经死亡、等待回对象池的尸体
            // 不参与出生点间距判断。
            if (health != null &&
                health.IsDead)
            {
                continue;
            }

            Vector3 difference =
                position -
                enemy.transform.position;

            difference.y = 0f;

            if (difference.sqrMagnitude <
                minSqrDistance)
            {
                return false;
            }
        }

        return true;
    }

    private void CleanupEnemies()
    {
        for (int i =
                 spawnedEnemies.Count - 1;
             i >= 0;
             i--)
        {
            GameObject enemy =
                spawnedEnemies[i];

            if (enemy == null ||
                !enemy.activeInHierarchy)
            {
                spawnedEnemies.RemoveAt(i);
                continue;
            }

            Health health =
                enemy.GetComponent<Health>();

            if (health != null &&
                health.IsDead)
            {
                spawnedEnemies.RemoveAt(i);
            }
        }
    }

    private void ResolveReferences()
    {
        if (poolManager == null)
        {
            poolManager =
                GameEntry.Pool;
        }

        if (player == null)
        {
            GameObject playerObject =
                GameObject.FindGameObjectWithTag(
                    "Player"
                );

            if (playerObject != null)
            {
                player =
                    playerObject.transform;
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (spawnPoints == null)
        {
            return;
        }

        foreach (Transform spawnPoint
                 in spawnPoints)
        {
            if (spawnPoint == null)
            {
                continue;
            }

            Gizmos.DrawWireSphere(
                spawnPoint.position,
                0.75f
            );
        }
    }
#endif
}