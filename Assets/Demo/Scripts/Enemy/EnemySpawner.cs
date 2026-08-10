using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class EnemySpawner : MonoBehaviour
{
    [Header("Enemy Prefabs")]
    [SerializeField] private GameObject[] enemyPrefabs;
    [SerializeField] private Transform player;

    [Header("Spawn Schedule")]
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField, Min(0)] private int initialSpawnCount = 6;
    [SerializeField, Min(1)] private int spawnCountPerBatch = 2;
    [SerializeField, Min(1)] private int maxAliveEnemies = 12;
    [SerializeField, Min(0f)] private float initialDelay = 1f;
    [SerializeField, Min(0.1f)] private float spawnInterval = 4f;

    [Header("Spawn Area")]
    [SerializeField, Min(0.1f)] private float spawnRadius = 25f;
    [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 4f;
    [SerializeField, Min(0f)] private float minDistanceFromPlayer = 8f;
    [SerializeField, Min(0f)] private float minEnemySpacing = 2f;
    [SerializeField, Min(1)] private int sampleAttemptsPerEnemy = 30;

    private readonly List<GameObject> spawnedEnemies = new();
    private PoolManager poolManager;
    private Coroutine spawnRoutine;

#if UNITY_EDITOR
    private void Reset()
    {
        enemyPrefabs = new[]
        {
            UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Enemy_Range.prefab"),
            UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Enemy_Melee.prefab")
        };
    }
#endif

    private void Start()
    {
        poolManager = GameEntry.Pool;

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            player = playerObject != null ? playerObject.transform : null;
        }

        if (!ValidatePrefabs() || poolManager == null)
        {
            enabled = false;
            return;
        }

        foreach (GameObject enemyPrefab in enemyPrefabs)
        {
            if (enemyPrefab != null)
            {
                poolManager.WarmEnemyPool(enemyPrefab);
            }
        }

        if (spawnOnStart)
        {
            spawnRoutine = StartCoroutine(SpawnLoop());
        }
    }

    private void OnDisable()
    {
        if (spawnRoutine != null)
        {
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }
    }

    private IEnumerator SpawnLoop()
    {
        if (initialDelay > 0f)
        {
            yield return new WaitForSeconds(initialDelay);
        }

        SpawnEnemies(initialSpawnCount);
        WaitForSeconds interval = new WaitForSeconds(spawnInterval);

        while (true)
        {
            yield return interval;
            SpawnEnemies(spawnCountPerBatch);
        }
    }

    [ContextMenu("Spawn Batch Now")]
    public void SpawnBatchNow()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (poolManager == null)
        {
            poolManager = GameEntry.Pool;
        }

        SpawnEnemies(spawnCountPerBatch);
    }

    private void SpawnEnemies(int requestedCount)
    {
        CleanupInactiveEnemies();

        int availableSlots = Mathf.Max(0, maxAliveEnemies - spawnedEnemies.Count);
        int countToSpawn = Mathf.Min(Mathf.Max(0, requestedCount), availableSlots);

        for (int i = 0; i < countToSpawn; i++)
        {
            if (!TryFindSpawnPoint(out Vector3 spawnPosition))
            {
                Debug.LogWarning(
                    $"{name} could not find a valid NavMesh spawn point.",
                    this);
                break;
            }

            GameObject prefab = GetRandomPrefab();

            if (prefab == null)
            {
                break;
            }

            Quaternion rotation = Quaternion.Euler(
                0f,
                Random.Range(0f, 360f),
                0f);

            GameObject enemy = poolManager.GetEnemy(
                prefab,
                spawnPosition,
                rotation);

            if (enemy != null)
            {
                spawnedEnemies.Add(enemy);
            }
        }
    }

    private bool TryFindSpawnPoint(out Vector3 spawnPosition)
    {
        for (int attempt = 0; attempt < sampleAttemptsPerEnemy; attempt++)
        {
            Vector2 randomOffset = Random.insideUnitCircle * spawnRadius;
            Vector3 candidate = transform.position +
                                new Vector3(randomOffset.x, 0f, randomOffset.y);

            if (!NavMesh.SamplePosition(
                    candidate,
                    out NavMeshHit hit,
                    navMeshSampleDistance,
                    NavMesh.AllAreas))
            {
                continue;
            }

            if (!IsFarEnoughFromPlayer(hit.position) ||
                !IsFarEnoughFromEnemies(hit.position))
            {
                continue;
            }

            spawnPosition = hit.position;
            return true;
        }

        spawnPosition = default;
        return false;
    }

    private bool IsFarEnoughFromPlayer(Vector3 position)
    {
        if (player == null || minDistanceFromPlayer <= 0f)
        {
            return true;
        }

        Vector3 difference = position - player.position;
        difference.y = 0f;
        return difference.sqrMagnitude >=
               minDistanceFromPlayer * minDistanceFromPlayer;
    }

    private bool IsFarEnoughFromEnemies(Vector3 position)
    {
        if (minEnemySpacing <= 0f)
        {
            return true;
        }

        float minimumSqrDistance = minEnemySpacing * minEnemySpacing;

        foreach (GameObject enemy in spawnedEnemies)
        {
            if (enemy == null || !enemy.activeInHierarchy)
            {
                continue;
            }

            Vector3 difference = position - enemy.transform.position;
            difference.y = 0f;

            if (difference.sqrMagnitude < minimumSqrDistance)
            {
                return false;
            }
        }

        return true;
    }

    private GameObject GetRandomPrefab()
    {
        int startIndex = Random.Range(0, enemyPrefabs.Length);

        for (int offset = 0; offset < enemyPrefabs.Length; offset++)
        {
            int index = (startIndex + offset) % enemyPrefabs.Length;
            GameObject prefab = enemyPrefabs[index];

            if (prefab != null)
            {
                return prefab;
            }
        }

        return null;
    }

    private void CleanupInactiveEnemies()
    {
        for (int i = spawnedEnemies.Count - 1; i >= 0; i--)
        {
            GameObject enemy = spawnedEnemies[i];

            if (enemy == null || !enemy.activeInHierarchy)
            {
                spawnedEnemies.RemoveAt(i);
            }
        }
    }

    private bool ValidatePrefabs()
    {
        if (enemyPrefabs == null || enemyPrefabs.Length == 0)
        {
            Debug.LogError("EnemySpawner has no enemy prefabs assigned.", this);
            return false;
        }

        foreach (GameObject enemyPrefab in enemyPrefabs)
        {
            if (enemyPrefab != null)
            {
                return true;
            }
        }

        Debug.LogError("EnemySpawner enemy prefab list only contains null entries.", this);
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, spawnRadius);
    }
}
