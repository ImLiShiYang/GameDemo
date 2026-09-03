using System.Collections.Generic;
using UnityEngine;

public sealed class ServerPlayerManager : MonoBehaviour
{
    private const float PlayerMoveSpeed = 3.2f;
    private const float FireDamage = 25f;
    private const float FireRange = 14f;
    private const int FireCooldownTicks = 5;
    private readonly Dictionary<int, ServerPlayer> players = new Dictionary<int, ServerPlayer>(2);

    private GameNetworkServer server;
    private ServerEntityRegistry entityRegistry;
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

    public void Initialize(GameNetworkServer networkServer, ServerEntityRegistry serverEntityRegistry)
    {
        server = networkServer;
        entityRegistry = serverEntityRegistry;
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
        if (!players.TryGetValue(playerId, out ServerPlayer player) || input.Sequence <= player.LastInputSequence)
        {
            return;
        }

        Vector2 movement = Vector2.ClampMagnitude(new Vector2(input.Horizontal, input.Vertical), 1f);
        Vector2 aim = Vector2.ClampMagnitude(new Vector2(input.AimX, input.AimZ), 1f);
        player.LastInputSequence = input.Sequence;
        player.MoveInput = movement;
        player.AimInput = aim;
        player.Buttons = input.Buttons & (ClientInputButtons.Fire | ClientInputButtons.Roll | ClientInputButtons.Skill1 | ClientInputButtons.Interact);
    }

    private void HandleServerTick(uint serverTick, float tickDeltaTime)
    {
        foreach (ServerPlayer player in players.Values)
        {
            SimulatePlayer(player, tickDeltaTime);
            SimulateFire(player, serverTick);
        }

        if (serverTick % snapshotTickInterval == 0)
        {
            server.BroadcastSnapshot(BuildSnapshot(serverTick));
        }
    }

    private static void SimulatePlayer(ServerPlayer player, float tickDeltaTime)
    {
        Vector3 movement = new Vector3(player.MoveInput.x, 0f, player.MoveInput.y);
        player.GameObject.transform.position += movement * (PlayerMoveSpeed * tickDeltaTime);

        if (player.AimInput.sqrMagnitude > 0.0001f)
        {
            Vector3 aimDirection = new Vector3(player.AimInput.x, 0f, player.AimInput.y);
            player.GameObject.transform.rotation = Quaternion.LookRotation(aimDirection, Vector3.up);
        }
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

        Vector3 origin = player.GameObject.transform.position + Vector3.up;
        entityRegistry?.TryApplyPlayerFire(player.EntityId, origin, direction.normalized, FireRange, FireDamage);
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
                MoveSpeed = player.MoveInput.magnitude * PlayerMoveSpeed,
                AnimationState = player.MoveInput.sqrMagnitude > 0.001f ? (byte)1 : (byte)0,
                LastProcessedInputSequence = player.LastInputSequence
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
        public uint LastInputSequence;
        public Vector2 MoveInput;
        public Vector2 AimInput;
        public ClientInputButtons Buttons;
        public uint NextFireTick;
    }
}
