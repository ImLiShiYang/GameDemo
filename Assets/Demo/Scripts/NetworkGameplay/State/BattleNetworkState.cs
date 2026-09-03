/// <summary>
/// 可被服务器快照完整覆盖的权威战斗流程状态。
/// </summary>
public sealed class BattleNetworkState
{
    public BattlePhase Phase = BattlePhase.WaitingForPlayers;
    public int CurrentWave;
    public int AliveEnemyCount;
    public bool AllEnemiesSpawned;
    public int BossEntityId;
    public uint ServerTick;

    public void CopyFrom(BattleNetworkState other)
    {
        if (other == null)
        {
            return;
        }

        Phase = other.Phase;
        CurrentWave = other.CurrentWave;
        AliveEnemyCount = other.AliveEnemyCount;
        AllEnemiesSpawned = other.AllEnemiesSpawned;
        BossEntityId = other.BossEntityId;
        ServerTick = other.ServerTick;
    }
}
