using UnityEngine;

/// <summary>
/// 双人网络战斗的服务器权威状态机。刷怪完成与存活数量均由显式计数维护。
/// </summary>
public sealed class ServerBattleFlow : MonoBehaviour
{
    private const int RequiredPlayerCount = 2;
    private const int TotalWaveCount = 2;
    private const int CountdownTicks = NetworkRuntime.DefaultTickRate * 3;
    private const int SpawnIntervalTicks = NetworkRuntime.DefaultTickRate / 2;
    private const int WaveClearedTicks = NetworkRuntime.DefaultTickRate * 2;
    private const int BossIntroTicks = NetworkRuntime.DefaultTickRate * 3;
    private const float EnemyHealth = 60f;
    private const float BossHealth = 400f;

    private readonly BattleNetworkState state = new BattleNetworkState();

    private GameNetworkServer server;
    private ServerEntityRegistry entities;
    private Vector3 battleOrigin;
    private uint phaseEndTick;
    private uint nextSpawnTick;
    private uint nextBossSpawnAttemptTick;
    private int enemiesToSpawn;
    private int spawnedEnemyCount;
    private bool scenePrepared;
    private bool bossSpawned;

    public BattleNetworkState State => state;
    public bool AllowsPlayerActions => state.Phase == BattlePhase.FightingEnemies || state.Phase == BattlePhase.FightingBoss;

    public void Initialize(GameNetworkServer networkServer, ServerEntityRegistry entityRegistry)
    {
        server = networkServer;
        entities = entityRegistry;
        entities.EntityDied += HandleEntityDied;
    }

    public void PrepareScene()
    {
        if (scenePrepared)
        {
            return;
        }

        GrayboxPlayerController playerTemplate = FindObjectOfType<GrayboxPlayerController>(true);
        battleOrigin = playerTemplate != null ? playerTemplate.transform.position : new Vector3(41.988f, 0.296f, 30.198f);
        scenePrepared = true;
        NetworkLog.Info("服务器战斗流程已准备，等待两名玩家连接。");
    }

    public void CopyStateTo(BattleNetworkState destination, uint serverTick)
    {
        state.ServerTick = serverTick;
        destination.CopyFrom(state);
    }

    private void OnDestroy()
    {
        if (entities != null)
        {
            entities.EntityDied -= HandleEntityDied;
        }
    }

    public void PrepareTick(uint serverTick)
    {
        if (!scenePrepared) return;
        if (state.Phase == BattlePhase.FightingEnemies) UpdateWaveSpawning(serverTick);
        if (state.Phase == BattlePhase.BossIntro && serverTick >= phaseEndTick && serverTick >= nextBossSpawnAttemptTick) SpawnBoss();
    }

    public void CompleteTick(uint serverTick)
    {
        if (!scenePrepared)
        {
            return;
        }

        state.ServerTick = serverTick;

        switch (state.Phase)
        {
            case BattlePhase.WaitingForPlayers:
                if (server.GetComponent<ServerPlayerManager>().PlayerCount >= RequiredPlayerCount)
                {
                    EnterCountdown(serverTick);
                }
                break;
            case BattlePhase.Countdown:
                if (server.GetComponent<ServerPlayerManager>().PlayerCount < RequiredPlayerCount)
                {
                    EnterWaitingForPlayers();
                }
                else if (serverTick >= phaseEndTick)
                {
                    StartWave(1, serverTick);
                }
                break;
            case BattlePhase.FightingEnemies:
                TryCompleteWave(serverTick);
                break;
            case BattlePhase.WaveCleared:
                if (serverTick >= phaseEndTick)
                {
                    if (state.CurrentWave < TotalWaveCount)
                    {
                        StartWave(state.CurrentWave + 1, serverTick);
                    }
                    else
                    {
                        EnterBossIntro(serverTick);
                    }
                }
                break;
            case BattlePhase.BossIntro:
                break;
        }
    }

    private void EnterWaitingForPlayers()
    {
        state.Phase = BattlePhase.WaitingForPlayers;
        phaseEndTick = 0;
        NetworkLog.Info("玩家数量不足，服务器返回等待阶段。");
    }

    private void EnterCountdown(uint serverTick)
    {
        state.Phase = BattlePhase.Countdown;
        state.CurrentWave = 0;
        state.AliveEnemyCount = 0;
        state.AllEnemiesSpawned = false;
        state.BossEntityId = 0;
        phaseEndTick = serverTick + CountdownTicks;
        BroadcastFlowEvent(BattleEventType.CountdownStarted);
        NetworkLog.Info($"两名玩家已连接，战斗将在 {CountdownTicks / NetworkRuntime.DefaultTickRate} 秒后开始。");
    }

    private void StartWave(int waveNumber, uint serverTick)
    {
        state.Phase = BattlePhase.FightingEnemies;
        state.CurrentWave = waveNumber;
        state.AliveEnemyCount = 0;
        state.AllEnemiesSpawned = false;
        enemiesToSpawn = waveNumber + 2;
        spawnedEnemyCount = 0;
        nextSpawnTick = serverTick;
        BroadcastFlowEvent(BattleEventType.WaveStarted);
        NetworkLog.Info($"服务器开始第 {waveNumber}/{TotalWaveCount} 波，共 {enemiesToSpawn} 只敌人。");
    }

    private void UpdateWaveSpawning(uint serverTick)
    {
        if (state.AllEnemiesSpawned || serverTick < nextSpawnTick)
        {
            return;
        }

        int spawnIndex = spawnedEnemyCount;
        float angle = spawnIndex * 137.5f + state.CurrentWave * 31f;
        Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * (5f + spawnIndex * 0.7f);
        GameObject enemyObject = new GameObject($"ServerEnemy_W{state.CurrentWave}_{spawnIndex + 1}");
        enemyObject.transform.SetPositionAndRotation(battleOrigin + offset, Quaternion.identity);
        int entityId = entities.Register(enemyObject, NetworkEntityType.Enemy, NetworkPrefabCatalog.TestEnemyPrefabId, 0,
            EnemyHealth, EnemyHealth);

        if (entityId == 0)
        {
            Destroy(enemyObject);
            nextSpawnTick = serverTick + SpawnIntervalTicks;
            return;
        }
        else
        {
            state.AliveEnemyCount++;
        }

        spawnedEnemyCount++;
        state.AllEnemiesSpawned = spawnedEnemyCount >= enemiesToSpawn;
        nextSpawnTick = serverTick + SpawnIntervalTicks;
    }

    private void TryCompleteWave(uint serverTick)
    {
        if (!state.AllEnemiesSpawned || state.AliveEnemyCount != 0)
        {
            return;
        }

        state.Phase = BattlePhase.WaveCleared;
        phaseEndTick = serverTick + WaveClearedTicks;
        BroadcastFlowEvent(BattleEventType.WaveCleared);
        NetworkLog.Info($"服务器确认第 {state.CurrentWave} 波清场。");
    }

    private void EnterBossIntro(uint serverTick)
    {
        state.Phase = BattlePhase.BossIntro;
        phaseEndTick = serverTick + BossIntroTicks;
        BroadcastFlowEvent(BattleEventType.BossIntroStarted);
        NetworkLog.Info("服务器进入 Boss 出场阶段。");
    }

    private void SpawnBoss()
    {
        if (bossSpawned)
        {
            return;
        }

        bossSpawned = true;
        GameObject bossObject = new GameObject("ServerBoss");
        bossObject.transform.SetPositionAndRotation(battleOrigin + Vector3.forward * 8f, Quaternion.Euler(0f, 180f, 0f));
        int entityId = entities.Register(bossObject, NetworkEntityType.Boss, NetworkPrefabCatalog.BossPrefabId, 0,
            BossHealth, BossHealth);

        if (entityId == 0)
        {
            bossSpawned = false;
            nextBossSpawnAttemptTick = server.ServerTick + SpawnIntervalTicks;
            Destroy(bossObject);
            NetworkLog.Error("服务器 Boss 注册失败，将在后续 Tick 重试。");
            return;
        }

        state.BossEntityId = entityId;
        state.Phase = BattlePhase.FightingBoss;
        BroadcastFlowEvent(BattleEventType.BossSpawned, entityId, bossObject.transform.position, BossHealth, BossHealth);
        NetworkLog.Info($"服务器生成 Boss，EntityId {entityId}。");
    }

    private void HandleEntityDied(int entityId, NetworkEntityType entityType, Vector3 position)
    {
        if (entityType == NetworkEntityType.Enemy && state.Phase == BattlePhase.FightingEnemies)
        {
            state.AliveEnemyCount = Mathf.Max(0, state.AliveEnemyCount - 1);
            TryCompleteWave(server.ServerTick);
            return;
        }

        if (entityType != NetworkEntityType.Boss || state.Phase != BattlePhase.FightingBoss || entityId != state.BossEntityId)
        {
            return;
        }

        BroadcastFlowEvent(BattleEventType.BossDied, entityId, position);
        state.Phase = BattlePhase.Finished;
        BroadcastFlowEvent(BattleEventType.BattleFinished, entityId, position);
        NetworkLog.Info($"Boss {entityId} 已死亡，服务器进入结算阶段。");
    }

    private void BroadcastFlowEvent(BattleEventType eventType, int targetEntityId = 0, Vector3 position = default,
        float currentHealth = 0f, float maxHealth = 0f)
    {
        server.BroadcastBattleEvent(new BattleEventMessage
        {
            EventType = eventType,
            TargetEntityId = targetEntityId,
            CurrentHealth = currentHealth,
            MaxHealth = maxHealth,
            Position = position,
            Phase = state.Phase,
            CurrentWave = state.CurrentWave
        });
    }
}
