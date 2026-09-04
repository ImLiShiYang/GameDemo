using System.Collections.Generic;
using UnityEngine;

public sealed class ServerPlayerManager : MonoBehaviour
{
    private const int MaxPendingInputs = 128;
    private const int InputHoldTicks = 4;
    private readonly Dictionary<int, ServerPlayer> players = new Dictionary<int, ServerPlayer>(2);
    private readonly SortedDictionary<int, int> pendingSpawns = new SortedDictionary<int, int>();
    private NetworkCharacterWorld characterWorld;

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
        characterWorld = NetworkCharacterWorld.GetOrCreate(gameObject);
        snapshotTickInterval = Mathf.Max(1, NetworkRuntime.DefaultTickRate / NetworkRuntime.DefaultSnapshotRate);
        server.PlayerAuthenticated += HandlePlayerAuthenticated;
        server.PlayerDisconnected += HandlePlayerDisconnected;
        server.ClientInputReceived += HandleClientInput;
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
    }

    private void HandlePlayerAuthenticated(int playerId, int entityId)
    {
        if (players.ContainsKey(playerId))
        {
            NetworkLog.Warning($"服务器忽略重复创建的 Player {playerId}。");
            return;
        }

        pendingSpawns[playerId] = entityId;
    }

    private bool TrySpawnPlayer(int playerId, int entityId)
    {
        Vector3 requested = spawnOrigin + Vector3.right * (playerId == 1 ? -1.25f : 1.25f);
        if (!characterWorld.TryFindSpawn(NetworkPrefabCatalog.PlayerPrefabId, requested, out Vector3 position)) return false;
        GameObject playerObject = CreatePlayerObject(playerId);
        playerObject.transform.position = position;
        NetworkEntity entity = playerObject.GetComponent<NetworkEntity>() ?? playerObject.AddComponent<NetworkEntity>();
        entity.Configure(entityId, NetworkEntityType.Player, NetworkPrefabCatalog.PlayerPrefabId, playerId, true);
        ServerPlayer player = new ServerPlayer(playerId, entityId, playerObject);
        player.Motor = characterWorld.Create(entityId, NetworkPrefabCatalog.PlayerPrefabId, position, true);
        players.Add(playerId, player);
        NetworkLog.Info($"服务器创建权威玩家 Player {playerId}，EntityId {entityId}，Position {playerObject.transform.position}。");
        return true;
    }

    private void HandlePlayerDisconnected(int playerId, int entityId)
    {
        pendingSpawns.Remove(playerId);
        characterWorld.Remove(entityId);
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
            Buttons = input.Buttons & (ClientInputButtons.Fire | ClientInputButtons.Roll | ClientInputButtons.Skill1 | ClientInputButtons.Skill2 | ClientInputButtons.Interact)
        };
        player.LastReceivedInputSequence = input.Sequence;
        player.PendingInputs.Enqueue(sanitizedInput);
    }

    public void PrepareTick(uint serverTick, List<int> movementOrder)
    {
        if (!scenePrepared) return;
        List<int> spawned = new List<int>();
        foreach (KeyValuePair<int, int> spawn in pendingSpawns)
            if (TrySpawnPlayer(spawn.Key, spawn.Value)) spawned.Add(spawn.Key);
        foreach (int id in spawned) pendingSpawns.Remove(id);
        foreach (ServerPlayer player in players.Values)
        {
            ConsumeNextInput(player, serverTick);
            player.Motor.SetBlocking(IsAlive(player));
            movementOrder.Add(player.EntityId);
        }
    }

    public bool MoveCharacter(int entityId)
    {
        foreach (ServerPlayer player in players.Values)
        {
            if (player.EntityId != entityId) continue;
            SimulatePlayer(player, IsAlive(player) && (battleFlow == null || battleFlow.AllowsPlayerActions));
            return true;
        }
        return false;
    }

    public void SimulateCombat(uint serverTick)
    {
        List<ServerPlayer> ordered = new List<ServerPlayer>(players.Values);
        ordered.Sort((a, b) => a.EntityId.CompareTo(b.EntityId));
        foreach (ServerPlayer player in ordered)
        {
            bool actionsAllowed = IsAlive(player) && (battleFlow == null || battleFlow.AllowsPlayerActions);

            if (actionsAllowed && !player.Action.IsRolling && player.Action.HitStunTicks == 0)
            {
                SimulateFire(player, serverTick);
                TryCastSkill(player, serverTick, 1);
                TryCastSkill(player, serverTick, 2);
            }
            else
            {
                player.MoveInput = Vector2.zero;
                player.Buttons = ClientInputButtons.None;
            }
            if (!IsAlive(player)) player.PendingShockWave = null;
            if (player.PendingShockWave != null && serverTick >= player.ShockWaveImpactTick)
            {
                SkillManager.SkillRuntimeConfig config = player.PendingShockWave;
                player.PendingShockWave = null;
                if (actionsAllowed) entityRegistry.ApplySkillDamage(player.EntityId, player.GameObject.transform.position,
                    player.GameObject.transform.forward, config.Range, config.Damage, config.InterruptPower, false);
            }
        }

    }

    public void SendSnapshot(uint serverTick)
    {
        if (scenePrepared && serverTick % snapshotTickInterval == 0)
        {
            server.BroadcastSnapshot(BuildSnapshot(serverTick));
        }
    }

    private static void ConsumeNextInput(ServerPlayer player, uint serverTick)
    {
        // Fire 是持续状态；翻滚和技能是单次按下，空 Tick 不能重复执行。
        player.Buttons &= ClientInputButtons.Fire;

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
            player.Buttons = ClientInputButtons.None;
        }
    }

    private static void SimulatePlayer(ServerPlayer player, bool actionsAllowed)
    {
        Vector3 position = player.Motor.State.Position;
        float rotationY = player.GameObject.transform.eulerAngles.y;
        Vector3 previousPosition = position;
        GrayboxPlayerController controller = player.GameObject.GetComponent<GrayboxPlayerController>();
        PlayerMovementSimulation.Step(ref position, ref rotationY, ref player.Action, player.MoveInput, player.AimInput, player.Buttons, actionsAllowed,
            controller != null ? controller.NetworkMoveSpeed : PlayerMovementSimulation.MoveSpeed,
            controller != null ? controller.NetworkAcceleration : 18f);
        position = player.Motor.Step(position - previousPosition, PlayerMovementSimulation.TickDeltaTime).Position;
        player.GameObject.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));
    }

    private void SimulateFire(ServerPlayer player, uint serverTick)
    {
        if ((player.Buttons & ClientInputButtons.Fire) == 0 || serverTick < player.NextFireTick || !IsAlive(player))
        {
            return;
        }

        GamePlayerAttack attack = player.GameObject.GetComponent<GamePlayerAttack>();
        PlayerCombatStats stats = player.GameObject.GetComponent<PlayerCombatStats>();
        float interval = (attack != null ? attack.NetworkAttackInterval : 0.2f) * (stats != null ? stats.FireIntervalMultiplier : 1f);
        player.NextFireTick = serverTick + (uint)Mathf.Max(1, Mathf.CeilToInt(interval * NetworkRuntime.DefaultTickRate));
        Vector3 direction = player.GameObject.transform.forward;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Vector3 origin = attack != null ? attack.NetworkMuzzlePosition : player.GameObject.transform.position + Vector3.up;
        int projectileEntityId = 0;
        int count = Mathf.Clamp(stats != null ? stats.ProjectileCount : 1, 1, 32);
        for (int i = 0; i < count; i++)
        {
            float spread = stats != null ? stats.SpreadAngle : 0f;
            float angle = count > 1 ? Mathf.Lerp(-spread * 0.5f, spread * 0.5f, i / (float)(count - 1)) : 0f;
            Vector3 shotDirection = Quaternion.AngleAxis(angle, Vector3.up) * direction.normalized;
            int id = projectileRegistry != null ? projectileRegistry.SpawnPlayerProjectile(player.PlayerId, player.EntityId,
                origin, shotDirection, attack != null ? attack.NetworkDamage : 10f, stats != null ? stats.PierceCount : 0,
                attack != null ? attack.NetworkProjectileSpeed : 15f, attack != null ? attack.NetworkProjectileLifetime : 3f) : 0;
            if (id > 0) projectileEntityId = id;
        }

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

    private void TryCastSkill(ServerPlayer player, uint tick, byte slot)
    {
        ClientInputButtons button = slot == 1 ? ClientInputButtons.Skill1 : ClientInputButtons.Skill2;
        uint nextTick = slot == 1 ? player.NextSkill1Tick : player.NextSkill2Tick;
        if ((player.Buttons & button) == 0 || tick < nextTick) return;
        SkillManager skills = GameEntry.Skill;
        string id = slot == 1 ? PlayerSkillInput.PrimarySkillId : PlayerSkillInput.SecondarySkillId;
        if (skills == null || !skills.TryGetSkillConfig(id, out SkillManager.SkillRuntimeConfig config)) return;
        uint readyTick = tick + (uint)Mathf.Max(1, Mathf.CeilToInt(config.Cooldown * NetworkRuntime.DefaultTickRate));
        if (slot == 1) player.NextSkill1Tick = readyTick;
        else player.NextSkill2Tick = readyTick;
        Transform caster = player.GameObject.transform;
        Vector3 origin = slot == 1 ? caster.position : player.GameObject.GetComponent<GamePlayerAttack>()?.NetworkMuzzlePosition ?? caster.position + Vector3.up;
        server.BroadcastBattleEvent(new BattleEventMessage
        {
            EventType = BattleEventType.PlayerSkillCast,
            SourceEntityId = player.EntityId,
            SkillSlot = slot,
            Position = origin,
            Direction = caster.forward,
            Range = config.Range,
            Duration = config.WarningTime,
            Phase = battleFlow?.State.Phase ?? BattlePhase.WaitingForPlayers,
            CurrentWave = battleFlow?.State.CurrentWave ?? 0
        });
        if (slot == 1)
        {
            player.PendingShockWave = config;
            player.ShockWaveImpactTick = tick + (uint)Mathf.CeilToInt(config.WarningTime * NetworkRuntime.DefaultTickRate);
        }
        else entityRegistry.ApplySkillDamage(player.EntityId, origin, caster.forward, config.Range, config.Damage, config.InterruptPower, true);
    }

    public void ApplyPlayerDamage(int entityId, in DamageInfo damage)
    {
        if (!NetworkRuntime.IsServer || damage.Amount <= 0f) return;
        foreach (ServerPlayer player in players.Values)
        {
            if (player.EntityId != entityId || !IsAlive(player) || player.Action.IsInvincible) continue;
            Health health = player.GameObject.GetComponent<Health>();
            if (health == null) return;
            float absorbed = Mathf.Min(health.CurrentShield, damage.Amount);
            float applied = Mathf.Min(health.CurrentHealth, damage.Amount - absorbed);
            health.ApplyNetworkState(health.CurrentHealth - applied, health.MaxHealth, health.CurrentShield - absorbed, health.ShieldCapacity);
            player.Motor.SetBlocking(!health.IsDead);
            if (applied > 0f)
            {
                player.Action.RollTicks = 0;
                player.Action.HitStunTicks = health.IsDead ? 0 : 8;
                NetworkEntity source = damage.Source != null ? damage.Source.GetComponent<NetworkEntity>() : null;
                server.BroadcastBattleEvent(new BattleEventMessage
                {
                    EventType = health.IsDead ? BattleEventType.EntityDied : BattleEventType.Damage,
                    SourceEntityId = source != null ? source.EntityId : 0,
                    TargetEntityId = entityId,
                    Amount = applied,
                    CurrentHealth = health.CurrentHealth,
                    MaxHealth = health.MaxHealth,
                    Position = player.GameObject.transform.position
                });
            }
            return;
        }
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
                VerticalVelocity = player.Motor.State.VerticalVelocity,
                Grounded = player.Motor.State.Grounded,
                RotationY = player.GameObject.transform.eulerAngles.y,
                CurrentHealth = health != null ? health.CurrentHealth : 100f,
                MoveSpeed = player.Action.MoveDirection.magnitude * (player.GameObject.GetComponent<GrayboxPlayerController>()?.NetworkMoveSpeed ?? PlayerMovementSimulation.MoveSpeed),
                AnimationState = player.MoveInput.sqrMagnitude > 0.001f ? (byte)1 : (byte)0,
                LastProcessedInputSequence = player.LastProcessedInputSequence,
                Action = player.Action,
                MaxHealth = health != null ? health.MaxHealth : 100f,
                Shield = health != null ? health.CurrentShield : 0f,
                ShieldCapacity = health != null ? health.ShieldCapacity : 0f,
                Skill1Cooldown = player.NextSkill1Tick > serverTick ? (player.NextSkill1Tick - serverTick) * PlayerMovementSimulation.TickDeltaTime : 0f,
                Skill2Cooldown = player.NextSkill2Tick > serverTick ? (player.NextSkill2Tick - serverTick) * PlayerMovementSimulation.TickDeltaTime : 0f,
                IsFiring = (player.Buttons & ClientInputButtons.Fire) != 0 && !player.Action.IsRolling && player.Action.HitStunTicks == 0 && IsAlive(player)
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
        foreach (Collider collider in playerObject.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
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
        public NetworkCharacterMotor Motor;
        public readonly Queue<ClientInputMessage> PendingInputs = new Queue<ClientInputMessage>(MaxPendingInputs);
        public uint LastReceivedInputSequence;
        public uint LastProcessedInputSequence;
        public uint LastConsumedInputTick;
        public Vector2 MoveInput;
        public Vector2 AimInput;
        public ClientInputButtons Buttons;
        public uint NextFireTick;
        public PlayerActionState Action;
        public uint NextSkill1Tick;
        public uint NextSkill2Tick;
        public uint ShockWaveImpactTick;
        public SkillManager.SkillRuntimeConfig PendingShockWave;
    }
}
