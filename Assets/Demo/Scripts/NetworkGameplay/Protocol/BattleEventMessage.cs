using UnityEngine;

/// <summary>
/// 服务器权威战斗事件。客户端只能接收并播放表现，不能提交此消息。
/// </summary>
public sealed class BattleEventMessage
{
    public BattleEventType EventType;
    public int SourceEntityId;
    public int TargetEntityId;
    public float Amount;
    public float CurrentHealth;
    public float MaxHealth;
    public Vector3 Position;
}
