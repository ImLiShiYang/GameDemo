/// <summary>
/// 服务器可靠广播的战斗事件类型。事件只描述已经由服务器确认的结果。
/// </summary>
public enum BattleEventType : byte
{
    Damage = 1,
    EntityDied = 2,
    PlayerFired = 3,
    CountdownStarted = 10,
    WaveStarted = 11,
    WaveCleared = 12,
    BossIntroStarted = 13,
    BossSpawned = 14,
    BossDied = 15,
    BattleFinished = 16
}
