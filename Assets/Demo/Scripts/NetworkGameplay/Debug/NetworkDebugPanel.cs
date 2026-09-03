using UnityEngine;

public sealed class NetworkDebugPanel : MonoBehaviour
{
    private GUIStyle style;

    private void OnGUI()
    {
        if (NetworkRuntime.IsOffline)
        {
            return;
        }

        style ??= new GUIStyle(GUI.skin.box)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 16,
            wordWrap = true
        };

        string connection = NetworkRuntime.IsServer
            ? $"监听中，客户端 {NetworkBootstrap.Instance?.Server?.ConnectedPlayerCount ?? 0}/2"
            : NetworkBootstrap.Instance?.Client?.StatusText ?? "未启动";
        int entityCount = NetworkRuntime.IsServer
            ? (NetworkBootstrap.Instance?.ServerPlayers?.PlayerCount ?? 0) +
              (NetworkBootstrap.Instance?.ServerEntities?.EntityCount ?? 0)
            : NetworkBootstrap.Instance?.ClientEntities?.EntityCount ?? 0;
        GameNetworkServer server = NetworkBootstrap.Instance?.Server;
        GameNetworkClient client = NetworkBootstrap.Instance?.Client;
        long sentMessages = NetworkRuntime.IsServer ? server?.SentMessageCount ?? 0 : client?.SentMessageCount ?? 0;
        long receivedMessages = NetworkRuntime.IsServer ? server?.ReceivedMessageCount ?? 0 : client?.ReceivedMessageCount ?? 0;
        string text =
            $"网络角色: {NetworkRuntime.Role}\n" +
            $"连接状态: {connection}\n" +
            $"PlayerId: {NetworkRuntime.LocalPlayerId}\n" +
            $"EntityId: {NetworkRuntime.LocalPlayerEntityId}\n" +
            $"MatchId: {NetworkRuntime.MatchId}\n" +
            $"ServerTick: {NetworkRuntime.ServerTick}\n" +
            $"SnapshotTick: {NetworkBootstrap.Instance?.Client?.LastSnapshotTick ?? 0}\n" +
            $"落后 Tick: {(NetworkRuntime.IsClient ? client?.SnapshotLagTicks ?? 0 : 0)}\n" +
            $"快照间隔: {(NetworkRuntime.IsClient ? client?.SecondsSinceLastSnapshot ?? 0f : 0f):0.00}s\n" +
            $"实体数量: {entityCount}\n" +
            $"战斗阶段: {ResolveBattlePhase()}\n" +
            $"消息: ↑{sentMessages} ↓{receivedMessages}";

        if (!string.IsNullOrEmpty(NetworkLog.LastError))
        {
            text += $"\n最后错误: {NetworkLog.LastError}";
        }

        GUI.Box(new Rect(12f, 12f, 390f, 270f), text, style);
    }

    private static BattlePhase ResolveBattlePhase()
    {
        if (NetworkRuntime.IsServer)
        {
            return NetworkBootstrap.Instance?.ServerBattle?.State.Phase ?? BattlePhase.WaitingForPlayers;
        }

        return NetworkBootstrap.Instance?.ClientBattle?.State.Phase ?? BattlePhase.WaitingForPlayers;
    }
}
