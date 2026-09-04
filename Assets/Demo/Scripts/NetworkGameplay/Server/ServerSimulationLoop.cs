using System.Collections.Generic;
using UnityEngine;

/// <summary>唯一游戏 Tick 调度者，快照只在本 Tick 全部模拟和结算完成后构建。</summary>
public sealed class ServerSimulationLoop : MonoBehaviour
{
    private GameNetworkServer server;
    private ServerPlayerManager players;
    private ServerEntityRegistry entities;
    private ServerProjectileRegistry projectiles;
    private ServerBattleFlow battle;
    private readonly List<int> movementOrder = new List<int>();

    public void Initialize(GameNetworkServer clock, ServerPlayerManager playerManager, ServerEntityRegistry registry,
        ServerProjectileRegistry projectileRegistry, ServerBattleFlow flow)
    {
        server = clock;
        players = playerManager;
        entities = registry;
        projectiles = projectileRegistry;
        battle = flow;
        server.ServerTicked += Tick;
    }

    private void Tick(uint tick, float deltaTime)
    {
        movementOrder.Clear();
        players.PrepareTick(tick, movementOrder);
        battle.PrepareTick(tick);
        entities.AppendMovementOrder(movementOrder);
        movementOrder.Sort();
        Physics.SyncTransforms();
        foreach (int id in movementOrder)
            if (!players.MoveCharacter(id)) entities.MoveCharacter(id, deltaTime);
        players.SimulateCombat(tick);
        projectiles.SimulateTick(tick, deltaTime);
        battle.CompleteTick(tick);
        players.SendSnapshot(tick);
    }

    private void OnDestroy() { if (server != null) server.ServerTicked -= Tick; }
}
