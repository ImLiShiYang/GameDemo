/// <summary>
/// 服务器可靠广播的战斗事件类型。事件只描述已经由服务器确认的结果。
/// </summary>
public enum BattleEventType : byte
{
    Damage = 1,
    EntityDied = 2
}
