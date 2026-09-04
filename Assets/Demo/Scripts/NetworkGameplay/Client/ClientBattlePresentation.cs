using UnityEngine;

/// <summary>
/// 把服务器战斗状态映射到客户端提示、音乐和结算 UI；不产生任何权威结果。
/// </summary>
public sealed class ClientBattlePresentation : MonoBehaviour
{
    private readonly BattleNetworkState state = new BattleNetworkState();

    private GameNetworkClient client;
    private GUIStyle bannerStyle;
    private bool victoryShown;

    public BattleNetworkState State => state;

    public void Initialize(GameNetworkClient networkClient)
    {
        client = networkClient;
        client.SnapshotReceived += HandleSnapshot;
        client.BattleEventReceived += HandleBattleEvent;
    }

    private void OnDestroy()
    {
        if (client == null)
        {
            return;
        }

        client.SnapshotReceived -= HandleSnapshot;
        client.BattleEventReceived -= HandleBattleEvent;
    }

    private void HandleSnapshot(WorldSnapshotMessage snapshot)
    {
        state.CopyFrom(snapshot.Battle);
    }

    private void HandleBattleEvent(BattleEventMessage message, uint serverTick)
    {
        if (message.EventType == BattleEventType.Damage || message.EventType == BattleEventType.EntityDied ||
            message.EventType == BattleEventType.PlayerFired || message.EventType == BattleEventType.PlayerSkillCast)
        {
            return;
        }

        state.Phase = message.Phase;
        state.CurrentWave = message.CurrentWave;
        state.ServerTick = serverTick;

        switch (message.EventType)
        {
            case BattleEventType.BossIntroStarted:
                GameAudioManager.Instance?.PlayBossMusic();
                NetworkLog.Info("客户端播放 Boss 出场提示和 Boss 音乐。");
                break;
            case BattleEventType.BossSpawned:
                state.BossEntityId = message.TargetEntityId;
                break;
            case BattleEventType.BattleFinished:
                ShowVictory();
                break;
        }
    }

    private void ShowVictory()
    {
        if (victoryShown)
        {
            return;
        }

        victoryShown = true;
        GameAudioManager.Instance?.PlayNormalMusic();
        GameResultController resultController = FindObjectOfType<GameResultController>(true);
        resultController?.ShowVictory();
        NetworkLog.Info("客户端收到服务器 BattleFinished，显示胜利结算。");
    }

    private void OnGUI()
    {
        if (!NetworkRuntime.IsClient || state.Phase == BattlePhase.WaitingForPlayers)
        {
            return;
        }

        string text = state.Phase switch
        {
            BattlePhase.Countdown => "两名玩家已就绪 · 战斗即将开始",
            BattlePhase.FightingEnemies => $"第 {state.CurrentWave} 波 · 剩余敌人 {state.AliveEnemyCount}",
            BattlePhase.WaveCleared => $"第 {state.CurrentWave} 波清除",
            BattlePhase.BossIntro => "警告 · BOSS 即将出现",
            BattlePhase.FightingBoss => "BOSS 战",
            BattlePhase.Finished => "战斗胜利",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        bannerStyle ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 24,
            fontStyle = FontStyle.Bold
        };
        GUI.Box(new Rect(Screen.width * 0.5f - 220f, 24f, 440f, 52f), text, bannerStyle);
    }
}
