using System;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-1000)]
public sealed class GameLockstepManager : MonoBehaviour
{
    private const int PositionScale = 1000;
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");

    private struct PlayerState
    {
        public int x;
        public int z;
        public Vector3 moveDirection;
    }

    private readonly LockstepFrameBuffer frameBuffer = new LockstepFrameBuffer();
    private LockstepNetworkClient networkClient;
    private LockstepNetworkServer networkServer;
    private GrayboxPlayerController localPlayer;
    private Transform remotePlayer;
    private Animator remoteAnimator;
    private PlayerState player1State;
    private PlayerState player2State;

    private int tickRate = 20;
    private int moveUnitsPerSecond = 2000;
    private int localPlayerId = 1;
    private int serverPort = 7777;
    private int currentTick;
    private int lastSentTick = -1;
    private bool simulationInitialized;
    private string serverAddress = "127.0.0.1";
    private bool runAsServer;
    private float accumulator;
    private float nextBindAttemptTime;
    private float nextReconnectTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreatePersistentManager()
    {
        if (FindObjectOfType<GameLockstepManager>() != null) return;

        GameObject root = new GameObject("[Lockstep]");
        DontDestroyOnLoad(root);
        root.AddComponent<GameLockstepManager>();
    }

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        Application.runInBackground = true;
        ApplyCommandLineOptions();
        SceneManager.sceneLoaded += HandleSceneLoaded;

        if (runAsServer)
        {
            networkServer = new LockstepNetworkServer();
            networkServer.Start(serverPort);
            Debug.Log($"[Lockstep Server] Listening on port {serverPort}.");
        }
        else
        {
            StartClient();
            TryBindGameplayScene();
        }
    }

    private void Update()
    {
        DrainNetworkLogs();
        if (runAsServer) return;

        if (networkClient == null || !networkClient.IsConnected)
        {
            if (Time.realtimeSinceStartup >= nextReconnectTime) StartClient();
        }

        if (localPlayer == null)
        {
            if (Time.realtimeSinceStartup >= nextBindAttemptTime)
            {
                nextBindAttemptTime = Time.realtimeSinceStartup + 0.5f;
                TryBindGameplayScene();
            }
            return;
        }

        DrainReceivedFrames();
        if (networkClient == null || !networkClient.IsConnected)
        {
            RenderPlayers();
            return;
        }

        float tickInterval = 1f / Mathf.Max(1, tickRate);
        accumulator += Time.unscaledDeltaTime;

        while (accumulator >= tickInterval)
        {
            if (lastSentTick != currentTick && networkClient.SendInput(new InputMessage(currentTick, localPlayerId, ReadLocalInput())))
            {
                lastSentTick = currentTick;
            }

            if (!frameBuffer.TryConsumeFrame(currentTick, out LockstepFrame frame))
            {
                accumulator = Mathf.Min(accumulator, tickInterval);
                break;
            }

            Simulate(frame);
            currentTick++;
            accumulator -= tickInterval;
        }

        RenderPlayers();
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        if (localPlayer != null) localPlayer.SetLockstepMovementEnabled(false);
        networkClient?.Dispose();
        networkServer?.Dispose();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!runAsServer) TryBindGameplayScene();
    }

    private void TryBindGameplayScene()
    {
        if (localPlayer != null) return;

        GrayboxPlayerController player = FindObjectOfType<GrayboxPlayerController>();
        if (player == null) return;

        localPlayer = player;
        CreateRemotePlayer(player.gameObject);

        Vector3 origin = player.transform.position;
        moveUnitsPerSecond = Mathf.RoundToInt(localPlayer.LockstepMoveSpeed * PositionScale);

        if (!simulationInitialized)
        {
            player1State = CreateState(origin);
            player2State = CreateState(origin + Vector3.right * 2f);
            currentTick = 0;
            lastSentTick = -1;
            frameBuffer.Clear();
            simulationInitialized = true;
        }

        accumulator = 0f;

        localPlayer.SetLockstepMovementEnabled(true);
        RenderPlayers();
        Debug.Log($"[Lockstep Client {localPlayerId}] Bound after scene load. Local={localPlayer.name}, Remote={remotePlayer.name}.");
    }

    private void CreateRemotePlayer(GameObject source)
    {
        GameObject clone = Instantiate(source, source.transform.position, source.transform.rotation);
        clone.name = $"Player_Graybox_Remote_P{(localPlayerId == 1 ? 2 : 1)}";
        clone.tag = "Untagged";

        foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
        foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (CharacterController controller in clone.GetComponentsInChildren<CharacterController>(true)) controller.enabled = false;
        foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true))
        {
            body.isKinematic = true;
            body.detectCollisions = false;
        }
        foreach (LineRenderer line in clone.GetComponentsInChildren<LineRenderer>(true)) line.enabled = false;

        remotePlayer = clone.transform;
        remoteAnimator = clone.GetComponentInChildren<Animator>(true);
    }

    private void StartClient()
    {
        networkClient?.Dispose();
        networkClient = new LockstepNetworkClient();
        networkClient.Start(serverAddress, serverPort);
        nextReconnectTime = Time.realtimeSinceStartup + 3f;
        Debug.Log($"[Lockstep Client {localPlayerId}] Connecting to {serverAddress}:{serverPort}.");
    }

    private void DrainReceivedFrames()
    {
        while (networkClient != null && networkClient.TryDequeueFrame(out LockstepFrame frame))
        {
            if (frame.tick >= currentTick) frameBuffer.SubmitFrame(frame);
        }
    }

    private void DrainNetworkLogs()
    {
        if (runAsServer)
        {
            while (networkServer != null && networkServer.TryDequeueLog(out string serverMessage)) Debug.Log($"[Lockstep Server] {serverMessage}");
            return;
        }

        while (networkClient != null && networkClient.TryDequeueLog(out string clientMessage)) Debug.Log($"[Lockstep Client {localPlayerId}] {clientMessage}");
    }

    private LockstepInput ReadLocalInput()
    {
        if (localPlayer == null || !localPlayer.CanAcceptLockstepMovementInput) 
            return default;

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Vector3 direction = localPlayer.GetCameraRelativeMoveDirection(horizontal, vertical);
        return new LockstepInput(Mathf.RoundToInt(direction.x * LockstepInput.AxisScale), Mathf.RoundToInt(direction.z * LockstepInput.AxisScale));
    }

    private void Simulate(LockstepFrame frame)
    {
        int moveUnitsPerTick = Mathf.RoundToInt(moveUnitsPerSecond / (float)Mathf.Max(1, tickRate));
        SimulatePlayer(ref player1State, frame.player1Input, moveUnitsPerTick);
        SimulatePlayer(ref player2State, frame.player2Input, moveUnitsPerTick);
    }

    private static void SimulatePlayer(ref PlayerState state, LockstepInput input, int moveUnitsPerTick)
    {
        state.x += DivideRounded(input.horizontal * moveUnitsPerTick, LockstepInput.AxisScale);
        state.z += DivideRounded(input.vertical * moveUnitsPerTick, LockstepInput.AxisScale);
        state.moveDirection = new Vector3(input.horizontal / (float)LockstepInput.AxisScale, 0f, input.vertical / (float)LockstepInput.AxisScale);
    }

    private void RenderPlayers()
    {
        if (localPlayer == null || remotePlayer == null) return;

        PlayerState localState = localPlayerId == 1 ? player1State : player2State;
        PlayerState remoteState = localPlayerId == 1 ? player2State : player1State;
        localPlayer.ApplyLockstepPose(ToWorldPosition(localState, localPlayer.transform.position.y), localState.moveDirection);
        remotePlayer.position = ToWorldPosition(remoteState, remotePlayer.position.y);
        UpdateRemotePresentation(remoteState.moveDirection);
    }

    private void UpdateRemotePresentation(Vector3 moveDirection)
    {
        float magnitude = Mathf.Clamp01(moveDirection.magnitude);
        if (moveDirection.sqrMagnitude > 0.0001f) remotePlayer.rotation = Quaternion.LookRotation(moveDirection, Vector3.up);
        if (remoteAnimator == null) return;

        remoteAnimator.SetFloat(MoveXHash, 0f, 0.08f, Time.deltaTime);
        remoteAnimator.SetFloat(MoveYHash, magnitude, 0.08f, Time.deltaTime);
    }

    private static PlayerState CreateState(Vector3 position)
    {
        return new PlayerState
        {
            x = Mathf.RoundToInt(position.x * PositionScale),
            z = Mathf.RoundToInt(position.z * PositionScale),
            moveDirection = Vector3.zero
        };
    }

    private static Vector3 ToWorldPosition(PlayerState state, float y)
    {
        return new Vector3(state.x / (float)PositionScale, y, state.z / (float)PositionScale);
    }

    private static int DivideRounded(int value, int divisor)
    {
        return value >= 0 ? (value + divisor / 2) / divisor : (value - divisor / 2) / divisor;
    }

    private void ApplyCommandLineOptions()
    {
        string[] arguments = Environment.GetCommandLineArgs();
        if (Array.Exists(arguments, argument => string.Equals(argument, "-lockstepServer", StringComparison.OrdinalIgnoreCase))) runAsServer = true;
        if (TryGetArgument(arguments, "-playerId", out string playerIdValue) && int.TryParse(playerIdValue, out int parsedPlayerId)) localPlayerId = Mathf.Clamp(parsedPlayerId, 1, 2);
        if (TryGetArgument(arguments, "-serverAddress", out string addressValue)) serverAddress = addressValue;
        if (TryGetArgument(arguments, "-serverPort", out string portValue) && int.TryParse(portValue, out int parsedPort)) serverPort = Mathf.Clamp(parsedPort, 1, 65535);
    }

    private static bool TryGetArgument(string[] arguments, string name, out string value)
    {
        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (!string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase)) continue;
            value = arguments[index + 1];
            return true;
        }

        value = null;
        return false;
    }
}
