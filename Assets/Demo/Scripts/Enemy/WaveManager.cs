using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WaveManager : MonoBehaviour
{
    /// <summary>
    /// 一种敌人的生成配置。
    ///
    /// 例如：
    /// Enemy_Melee
    /// Count = 6
    /// </summary>
    [Serializable]
    public class EnemySpawnEntry
    {
        [SerializeField]
        private GameObject enemyPrefab;

        [SerializeField, Min(1)]
        private int count = 1;

        public GameObject EnemyPrefab =>enemyPrefab;

        public int Count =>count;
    }

    /// <summary>
    /// 一整波的配置。
    /// </summary>
    [Serializable]
    public class WaveConfig
    {
        [SerializeField]
        private string waveName =
            "Wave";

        [SerializeField, Min(0.05f)]
        [Tooltip("刷怪间隔")]
        private float spawnInterval = 0.5f;
           

        [SerializeField]
        private List<EnemySpawnEntry> enemies = new();

        public string WaveName =>
            waveName;

        public float SpawnInterval =>
            spawnInterval;

        public List<EnemySpawnEntry> Enemies => enemies;
            
    }

    [Header("References")]
    [SerializeField]
    private EnemySpawner enemySpawner;

    [Header("Wave Config")]
    [SerializeField]
    private List<WaveConfig> waves = new();

    [Header("Timing")]
    [SerializeField, Min(0f)]
    private float firstWaveDelay = 1f;

    [SerializeField, Min(0f)]
    private float nextWaveDelay = 3f;

    [Header("Boss")]
    [SerializeField] private GameObject bossPrefab;
    [SerializeField] private Transform bossSpawnPoint;
    [SerializeField, Min(0f)] private float bossSpawnDelay = 2f;
    [SerializeField, Min(0f)]
    [Tooltip("Boss 死亡后，显示胜利结算前的停留时间。")]
    private float victoryDelay = 3f;
    [SerializeField] private string bossName = "ABYSS GUARDIAN";

    private Health currentBossHealth;


    [Header("Spawn")]
    [Tooltip(
        "开启后，同一波的近战/远程怪会混合生成，而不是先刷完一种再刷另一种。"
    )]
    [SerializeField]
    private bool shuffleSpawnOrder = true;

    private Coroutine waveRoutine;

    private int currentWaveIndex = -1;

    private int aliveEnemyCount;

    private bool isRunning;

    private bool isVictory;

    /// <summary>
    /// 记录当前 WaveManager
    /// 给哪些 Health 注册了死亡事件。
    /// </summary>
    private readonly Dictionary<
        Health,
        Action
    > deathHandlers = new();

    public int CurrentWaveNumber =>
        currentWaveIndex + 1;

    public int TotalWaveCount =>
        waves.Count;

    public int AliveEnemyCount =>
        aliveEnemyCount;

    public bool IsRunning =>
        isRunning;

    public bool IsVictory =>
        isVictory;

    /// <summary>
    /// 参数：
    /// 当前波
    /// 总波数
    /// </summary>
    public event Action<int,int> WaveStarted;
        

    /// <summary>
    /// 当前存活怪数量变化。
    /// </summary>
    public event Action<int> AliveEnemyCountChanged;

    /// <summary>
    /// 全部波次结束。
    /// 后面结算 UI 可以监听这个事件。
    /// </summary>
    public event Action Victory;
    
    public event Action<string, Health> BossSpawned;

    /// <summary>
    /// 开始整场波次。
    /// </summary>
    public void StartWaves()
    {
        if (isRunning)
        {
            return;
        }

        if (enemySpawner == null)
        {
            Debug.LogError(
                "WaveManager 没有设置 EnemySpawner。",
                this
            );

            return;
        }

        if (waves == null ||
            waves.Count == 0)
        {
            Debug.LogError(
                "WaveManager 没有配置任何 Wave。",
                this
            );

            return;
        }

        ClearDeathSubscriptions();

        currentWaveIndex = -1;
        aliveEnemyCount = 0;
        isVictory = false;
        isRunning = true;

        waveRoutine =StartCoroutine(RunWaves());
    }

    /// <summary>
    /// 整场战斗的大循环。
    /// </summary>
    private IEnumerator RunWaves()
    {
        if (firstWaveDelay > 0f)
        {
            yield return new WaitForSeconds(firstWaveDelay);
        }

        for (int i = 0;i < waves.Count;i++)
        {
            currentWaveIndex = i;

            WaveConfig wave = waves[i];

            Debug.Log(
                $"开始第 {CurrentWaveNumber}/{TotalWaveCount} 波：{wave.WaveName}",
                this
            );

            WaveStarted?.Invoke(CurrentWaveNumber,TotalWaveCount);

            // 开始刷这一波的所有怪。
            yield return SpawnWave(wave);

            /*
             * 注意：
             *
             * SpawnWave 结束只表示
             * “这一波所有怪已经刷出来了”
             *
             * 并不代表怪都死了。
             *
             * 所以这里继续等：
             */
            while (aliveEnemyCount > 0)
            {
                yield return null;
            }

            Debug.Log(
                $"第 {CurrentWaveNumber} 波结束。",
                this
            );

            // 最后一波不需要等待，
            // 直接进入胜利。
            bool isLastWave = i >= waves.Count - 1;
                

            if (isLastWave)
            {
                break;
            }

            // 当前波全部死亡后，
            // 等几秒再开始下一波。
            if (nextWaveDelay > 0f)
            {
                yield return new WaitForSeconds(nextWaveDelay);
            }
        }

        if (bossSpawnDelay > 0f)
        {
            yield return new WaitForSeconds(bossSpawnDelay);
        }

        currentBossHealth = SpawnBoss();

        if (currentBossHealth == null)
        {
            waveRoutine = null;
            isRunning = false;
            yield break;
        }

        BossSpawned?.Invoke(bossName, currentBossHealth);

        while (!currentBossHealth.IsDead)
        {
            yield return null;
        }

        currentBossHealth = null;

        if (victoryDelay > 0f)
        {
            yield return new WaitForSeconds(victoryDelay);
        }

        CompleteVictory();
    }

    private Health SpawnBoss()
    {
        if (bossPrefab == null)
        {
            Debug.LogError("WaveManager 没有设置 Boss Prefab。", this);
            return null;
        }

        if (bossSpawnPoint == null)
        {
            Debug.LogError("WaveManager 没有设置 Boss Spawn Point。", this);
            return null;
        }

        GameObject boss = Instantiate(bossPrefab, bossSpawnPoint.position, bossSpawnPoint.rotation);

        Health health = boss.GetComponent<Health>();

        if (health == null)
        {
            Debug.LogError($"{boss.name} 没有 Health。", boss);
            Destroy(boss);
            return null;
        }

        Debug.Log($"Boss 生成：{boss.name}", boss);

        return health;
    }

    /// <summary>
    /// 负责把当前波配置里的敌人
    /// 一个一个交给 EnemySpawner。
    /// </summary>
    private IEnumerator SpawnWave(WaveConfig wave)
    {
        List<GameObject> spawnQueue =
            BuildSpawnQueue(wave);

        for (int i = 0;
             i < spawnQueue.Count;
             i++)
        {
            GameObject enemyPrefab =
                spawnQueue[i];

            GameObject enemy =
                enemySpawner.Spawn(
                    enemyPrefab
                );

            if (enemy != null)
            {
                RegisterEnemy(enemy);
            }
            else
            {
                Debug.LogWarning(
                    $"第 {CurrentWaveNumber} 波生成敌人失败：{enemyPrefab?.name}",
                    this
                );
            }

            // 最后一只后面不再等待
            if (i >=
                spawnQueue.Count - 1)
            {
                continue;
            }

            if (wave.SpawnInterval > 0f)
            {
                yield return
                    new WaitForSeconds(
                        wave.SpawnInterval
                    );
            }
        }
    }

    /// <summary>
    /// 根据这一波的配置，
    /// 生成真正的刷怪队列。
    ///
    /// 例如：
    ///
    /// Melee x 3
    /// Range x 2
    ///
    /// 最终：
    ///
    /// Melee
    /// Melee
    /// Melee
    /// Range
    /// Range
    /// </summary>
    private List<GameObject>
        BuildSpawnQueue(
            WaveConfig wave)
    {
        List<GameObject> queue =
            new();

        if (wave == null ||
            wave.Enemies == null)
        {
            return queue;
        }

        foreach (
            EnemySpawnEntry entry
            in wave.Enemies)
        {
            if (entry == null ||
                entry.EnemyPrefab == null ||
                entry.Count <= 0)
            {
                continue;
            }

            for (int i = 0;
                 i < entry.Count;
                 i++)
            {
                queue.Add(
                    entry.EnemyPrefab
                );
            }
        }

        if (shuffleSpawnOrder)
        {
            Shuffle(queue);
        }

        return queue;
    }

    /// <summary>
    /// WaveManager 开始监视这个敌人的死亡。
    /// </summary>
    private void RegisterEnemy(GameObject enemy)
    {
        Health health =enemy.GetComponent<Health>();

        if (health == null)
        {
            Debug.LogError(
                $"{enemy.name} 没有 Health。",
                enemy
            );

            return;
        }

        if (deathHandlers.ContainsKey(health))
        {
            return;
        }

        aliveEnemyCount++;
        AliveEnemyCountChanged?.Invoke(aliveEnemyCount);

        Action handler = null;

        handler = () =>
        {
            OnEnemyDied(health);
        };

        deathHandlers.Add(
            health,
            handler
        );

        health.Died += handler;

        Debug.Log(
            $"敌人生成，当前存活：{aliveEnemyCount}",
            this
        );
    }

    /// <summary>
    /// 某一只敌人死亡。
    /// </summary>
    private void OnEnemyDied(Health health)
    {
        if (health == null)
        {
            return;
        }

        if (!deathHandlers.TryGetValue(
                health,
                out Action handler))
        {
            return;
        }

        // 解除事件
        health.Died -= handler;

        deathHandlers.Remove(health);

        aliveEnemyCount =Mathf.Max(0,aliveEnemyCount - 1);
        AliveEnemyCountChanged?.Invoke(aliveEnemyCount);

        Debug.Log(
            $"敌人死亡，当前波剩余：{aliveEnemyCount}",
            this
        );
    }

    private void CompleteVictory()
    {
        waveRoutine = null;
        isRunning = false;
        isVictory = true;

        Debug.Log(
            "所有波次结束，玩家胜利！",
            this
        );

        Victory?.Invoke();
    }

    /// <summary>
    /// Fisher-Yates 洗牌。
    /// 用于让近战和远程混着出来。
    /// </summary>
    private static void Shuffle(
        List<GameObject> list)
    {
        for (int i =
                 list.Count - 1;
             i > 0;
             i--)
        {
            int randomIndex =
                UnityEngine.Random.Range(
                    0,
                    i + 1
                );

            (list[i],
             list[randomIndex]) =
                (list[randomIndex],
                 list[i]);
        }
    }

    private void ClearDeathSubscriptions()
    {
        foreach (KeyValuePair<Health, Action> pair in deathHandlers)
        {
            if (pair.Key != null)
            {
                pair.Key.Died -= pair.Value;
            }
        }

        deathHandlers.Clear();
    }

    private void OnDisable()
    {
        if (waveRoutine != null)
        {
            StopCoroutine(
                waveRoutine
            );

            waveRoutine = null;
        }

        ClearDeathSubscriptions();

        isRunning = false;
    }
}
