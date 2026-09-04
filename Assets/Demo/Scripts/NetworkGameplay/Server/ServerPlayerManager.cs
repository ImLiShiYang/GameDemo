using System.Collections.Generic;
using UnityEngine;

public sealed class ServerPlayerManager : MonoBehaviour
{
    private const int FireCooldownTicks = 5;
    private const int MaxPendingInputs = 128;
    private const int InputHoldTicks = 4;
    private readonly Dictionary<int, ServerPlayer> players = new Dictionary<int, ServerPlayer>(2);

    private GameNetworkServer server;
    private ServerEntityRegistry entityRegistry;
    private ServerProjectileRegistry projectileRegistry;
    private ServerBattleFlow battleFlow;
    private GameObject playerTemplate;
    private Vector3 spawnOrigin = new Vector3(41.988f, 0.296f, 30.198f);
    private Quaternion spawnRotation = Quaternion.identity;
    private int snapshotTickInterval = 2;
    private bool scenePrepared;

    public int PlayerCount => players.Count;

    public bool TryGetClosestAlivePlayer(Vector3 position, out Transform playerTransform, out int playerEntityId)
    {
        playerTransform = null;
        playerEntityId = 0;
        float closestDistanceSquared = float.PositiveInfinity;

        foreach (ServerPlayer player in players.Values)
        {
            if (!IsAlive(player))
            {
                continue;
            }

            float distanceSquared = (player.GameObject.transform.position - position).sqrMagnitude;

            if (distanceSquared >= closestDistanceSquared)
            {
                continue;
            }

            closestDistanceSquared = distanceSquared;
            playerTransform = player.GameObject.transform;
            playerEntityId = player.EntityId;
        }

        return playerTransform != null;
    }

    public void Initialize(GameNetworkServer networkServer, ServerEntityRegistry serverEntityRegistry,
        ServerProjectileRegistry serverProjectileRegistry, ServerBattleFlow serverBattleFlow)
    {
        server = networkServer;
        entityRegistry = serverEntityRegistry;
        projectileRegistry = serverProjectileRegistry;
        battleFlow = serverBattleFlow;
        snapshotTickInterval = Mathf.Max(1, NetworkRuntime.DefaultTickRate / NetworkRuntime.DefaultSnapshotRate);
        server.PlayerAuthenticated += HandlePlayerAuthenticated;
        server.PlayerDisconnected += HandlePlayerDisconnected;
        server.ClientInputReceived += HandleClientInput;
        server.ServerTicked += HandleServerTick;
    }

    public void PrepareScene()
    {
        if (scenePrepared)
        {
            return;
        }

        GrayboxPlayerController controller = FindObjectOfType<GrayboxPlayerController>(true);

        if (controller == null)
        {
            NetworkLog.Warning("服务器场景没有找到玩家表现模板，将使用空对象模拟权威玩家。");
            return;
        }

        playerTemplate = controller.gameObject;
        scenePrepared = true;
        spawnOrigin = playerTemplate.transform.position;
        spawnRotation = playerTemplate.transform.rotation;
        DisableServerGameplay(playerTemplate);
        playerTemplate.SetActive(false);
        NetworkLog.Info($"服务器玩家出生点已准备：{spawnOrigin}。");
    }

    private void OnDestroy()
    {
        if (server == null)
        {
            return;
        }

        server.PlayerAuthenticated -= HandlePlayerAuthenticated;
        server.PlayerDisconnected -= HandlePlayerDisconnected;
        server.ClientInputReceived -= HandleClientInput;
        server.ServerTicked -= HandleServerTick;
    }

    private void HandlePlayerAuthenticated(int playerId, int entityId)
    {
        if (players.ContainsKey(playerId))
        {
            NetworkLog.Warning($"服务器忽略重复创建的 Player {playerId}。");
            return;
        }

        GameObject playerObject = CreatePlayerObject(playerId);
        NetworkEntity entity = playerObject.GetComponent<NetworkEntity>() ?? playerObject.AddComponent<NetworkEntity>();
        entity.Configure(entityId, NetworkEntityType.Player, NetworkPrefabCatalog.PlayerPrefabId, playerId, true);
        ServerPlayer player = new ServerPlayer(playerId, entityId, playerObject);
        players.Add(playerId, player);
        NetworkLog.Info($"服务器创建权威玩家 Player {playerId}，EntityId {entityId}，Position {playerObject.transform.position}。");
    }

    private void HandlePlayerDisconnected(int playerId, int entityId)
    {
        if (!players.TryGetValue(playerId, out ServerPlayer player))
        {
            return;
        }

        players.Remove(playerId);

        if (player.GameObject == playerTemplate)
        {
            player.GameObject.SetActive(false);
        }
        else
        {
            Destroy(player.GameObject);
        }
    }

    private void HandleClientInput(int playerId, ClientInputMessage input)
    {
        if (!players.TryGetValue(playerId, out ServerPlayer player) || input.Sequence <= player.LastReceivedInputSequence)
        {
            return;
        }

        if (player.PendingInputs.Count >= MaxPendingInputs)
        {
            NetworkLog.Warning($"Player {playerId} 输入队列已满，丢弃 Sequence {input.Sequence}。");
            return;
        }

        ClientInputMessage sanitizedInput = new ClientInputMessage
        {
            Sequence = input.Sequence,
            ClientTick = input.ClientTick,
            Horizontal = Mathf.Clamp(input.Horizontal, -1f, 1f),
            Vertical = Mathf.Clamp(input.Vertical, -1f, 1f),
            AimX = Mathf.Clamp(input.AimX, -1f, 1f),
            AimZ = Mathf.Clamp(input.AimZ, -1f, 1f),
            Buttons = input.Buttons & (ClientInputButtons.Fire | ClientInputButtons.Roll | ClientInputButtons.Skill1 | ClientInputButtons.Interact)
        };
        player.LastReceivedInputSequence = input.Sequence;
        player.PendingInputs.Enqueue(sanitizedInput);
    }

    private void HandleServerTick(uint serverTick, float _)
    {
        foreach (ServerPlayer player in players.Values)
        {
            ConsumeNextInput(player, serverTick);
            bool actionsAllowed = battleFlow == null || battleFlow.AllowsPlayerActions;

            if (actionsAllowed)
            {
                SimulatePlayer(player);
                SimulateFire(player, serverTick);
            }
            else
            {
                player.MoveInput = Vector2.zero;
                player.Buttons = ClientInputButtons.None;
            }
        }

        if (serverTick % snapshotTickInterval == 0)
        {
            server.BroadcastSnapshot(BuildSnapshot(serverTick));
        }
    }

    private static void ConsumeNextInput(ServerPlayer player, uint serverTick)
    {
        player.Buttons = ClientInputButtons.None;

        if (player.PendingInputs.Count > 0)
        {
            ClientInputMessage input = player.PendingInputs.Dequeue();
            player.MoveInput = Vector2.ClampMagnitude(new Vector2(input.Horizontal, input.Vertical), 1f);
            player.AimInput = Vector2.ClampMagnitude(new Vector2(input.AimX, input.AimZ), 1f);
            player.Buttons = input.Buttons;
            player.LastProcessedInputSequence = input.Sequence;
            player.LastConsumedInputTick = serverTick;
        }
        else if (serverTick - player.LastConsumedInputTick > InputHoldTicks)
        {
            player.MoveInput = Vector2.zero;
        }
    }

    private static void SimulatePlayer(ServerPlayer player)
    {
        Vector3 position = player.GameObject.transform.position;
        float rotationY = player.GameObject.transform.eulerAngles.y;
        PlayerMovementSimulation.Step(ref position, ref rotationY, player.MoveInput, player.AimInput);
        player.GameObject.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));
    }

    private void SimulateFire(ServerPlayer player, uint serverTick)
    {
        if ((player.Buttons & ClientInputButtons.Fire) == 0 || serverTick < player.NextFireTick || !IsAlive(player))
        {
            return;
        }

        player.NextFireTick = serverTick + FireCooldownTicks;
        Vector3 direction = player.GameObject.transform.forward;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 origin = player.GameObject.transform.position + Vector3.up + direction.normalized * 0.8f;
        int projectileEntityId = projectileRegistry != null
            ? projectileRegistry.SpawnPlayerProjectile(player.PlayerId, player.EntityId, origin, direction.normalized)
            : 0;

        if (projectileEntityId > 0)
        {
            BattleNetworkState battleState = battleFlow?.State;
            server.BroadcastBattleEvent(new BattleEventMessage
            {
                EventType = BattleEventType.PlayerFired,
                SourceEntityId = player.EntityId,
                TargetEntityId = projectileEntityId,
                Position = origin,
                Phase = battleState?.Phase ?? BattlePhase.WaitingForPlayers,
                CurrentWave = battleState?.CurrentWave ?? 0
            });
        }
    }

    private static bool IsAlive(ServerPlayer player)
    {
        if (player?.GameObject == null)
        {
            return false;
        }

        Health health = player.GameObject.GetComponent<Health>();
        return health == null || !health.IsDead;
    }

    private WorldSnapshotMessage BuildSnapshot(uint serverTick)
    {
        WorldSnapshotMessage snapshot = new WorldSnapshotMessage { ServerTick = serverTick };
        battleFlow?.CopyStateTo(snapshot.Battle, serverTick);

        foreach (ServerPlayer player in players.Values)
        {
            Health health = player.GameObject.GetComponent<Health>();
            snapshot.Players.Add(new PlayerNetworkState
            {
                EntityId = player.EntityId,
                OwnerPlayerId = player.PlayerId,
                Position = player.GameObject.transform.position,
                RotationY = player.GameObject.transform.eulerAngles.y,
                CurrentHealth = health != null ? health.CurrentHealth : 100f,
                MoveSpeed = player.MoveInput.magnitude * PlayerMovementSimulation.MoveSpeed,
                AnimationState = player.MoveInput.sqrMagnitude > 0.001f ? (byte)1 : (byte)0,
                LastProcessedInputSequence = player.LastProcessedInputSequence
            });
        }

        entityRegistry?.AppendSnapshot(snapshot);
        return snapshot;
    }

    private GameObject CreatePlayerObject(int playerId)
    {
        GameObject playerObject;

        if (playerTemplate != null && playerId == 1 && !playerTemplate.activeSelf)
        {
            playerObject = playerTemplate;
        }
        else if (playerTemplate != null)
        {
            playerObject = Instantiate(playerTemplate);
            DisableServerGameplay(playerObject);
        }
        else
        {
            playerObject = new GameObject();
        }

        playerObject.name = $"ServerPlayer_{playerId}";
        playerObject.transform.SetPositionAndRotation(
            spawnOrigin + Vector3.right * (playerId == 1 ? -1.25f : 1.25f),
            spawnRotation
        );
        playerObject.SetActive(true);
        return playerObject;
    }

    private static void DisableServerGameplay(GameObject playerObject)
    {
        foreach (MonoBehaviour behaviour in playerObject.GetComponentsInChildren<MonoBehaviour>(true))
        {
            behaviour.enabled = false;
        }

        foreach (Animator animator in playerObject.GetComponentsInChildren<Animator>(true))
        {
            animator.enabled = false;
        }

        foreach (AudioSource audioSource in playerObject.GetComponentsInChildren<AudioSource>(true))
        {
            audioSource.enabled = false;
        }

        CharacterController characterController = playerObject.GetComponent<CharacterController>();

        if (characterController != null)
        {
            characterController.enabled = false;
        }
    }

    private sealed class ServerPlayer
    {
        public ServerPlayer(int playerId, int entityId, GameObject gameObject)
        {
            PlayerId = playerId;
            EntityId = entityId;
            GameObject = gameObject;
        }

        public int PlayerId { get; }
        public int EntityId { get; }
        public GameObject GameObject { get; }
        public readonly Queue<ClientInputMessage> PendingInputs = new Queue<ClientInputMessage>(MaxPendingInputs);
        public uint LastReceivedInputSequence;
        public uint LastProcessedInputSequence;
        public uint LastConsumedInputTick;
        public Vector2 MoveInput;
        public Vector2 AimInput;
        public ClientInputButtons Buttons;
        public uint NextFireTick;
    }
}
