using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;


/// <summary>
/// 网络模式的总入口。
/// 程序启动时根据命令行创建自身，并在同一个常驻 GameObject 上装配服务器或客户端网络组件。
/// Server 模式立即启动游戏服务器并加载主场景；Client 模式必须等待登录成功和 Welcome 后才加载主场景。
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class NetworkBootstrap : MonoBehaviour
{
    /// <summary>
    /// 从命令行解析出的本次进程运行方式；不使用 Inspector 配置，便于同一份 Build 启动服务器和多个客户端。
    /// </summary>
    private sealed class LaunchOptions
    {
        // 本进程的网络角色：Offline、Server 或 Client。
        public NetworkRole Role;
        // Client 要连接的游戏服务器地址，默认本机回环地址。
        public string ServerAddress = NetworkRuntime.DefaultServerAddress;
        // Client 连接端口或 Server 监听端口，默认 7777。
        public int ServerPort = NetworkRuntime.DefaultServerPort;
        // Client 申请的玩家席位；MVP 中仅允许 1 或 2。
        public int PlayerId;
        // 可由命令行指定的显示名称；登录昵称存在时会优先使用登录昵称。
        public string PlayerName;
    }

    // 本次启动从命令行得到的配置。
    private LaunchOptions options;
    // 防止 OnApplicationQuit 和 OnDestroy 重复关闭同一批网络资源。
    private bool shuttingDown;

    // 全局唯一的网络总控实例，供登录界面和调试面板取得网络状态。
    public static NetworkBootstrap Instance { get; private set; }
    // Server 模式下挂在本对象上的 TCP 服务端组件。
    public GameNetworkServer Server { get; private set; }
    // Client 模式下、登录成功后才挂在本对象上的 TCP 客户端组件。
    public GameNetworkClient Client { get; private set; }
    // Server 模式下维护权威玩家对象、输入和快照的组件。
    public ServerPlayerManager ServerPlayers { get; private set; }
    // Server 模式下分配通用 EntityId，并负责实体 Spawn、Despawn 和通用快照状态。
    public ServerEntityRegistry ServerEntities { get; private set; }
    // Server 模式下创建和推进权威子弹，并负责命中、伤害和生命周期。
    public ServerProjectileRegistry ServerProjectiles { get; private set; }
    // Server 模式下维护等待、波次、Boss 与结算的权威状态机。
    public ServerBattleFlow ServerBattle { get; private set; }
    // Client 模式下把服务器快照映射成场景表现对象的组件。
    public ClientEntityRegistry ClientEntities { get; private set; }
    // Client 模式下把服务器战斗事件映射为提示、音乐和结算界面。
    public ClientBattlePresentation ClientBattle { get; private set; }
    // Client 模式下预测本地玩家移动，并按服务器确认序号执行校正与输入重演。
    public ClientPlayerPrediction ClientPrediction { get; private set; }

    /// <summary>
    /// Unity 在加载首个场景前调用的真正网络入口。
    /// 只有带 -gameServer 或 -gameClient 参数的进程会创建 NetworkBootstrap；普通单机运行不会创建它。
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateFromCommandLine()
    {
        // 读取 exe 启动参数，决定本次运行是服务器、客户端还是普通单机。
        LaunchOptions launchOptions = ParseCommandLine(Environment.GetCommandLineArgs());

        // Offline 不需要网络总控；Instance 检查避免 Unity 生命周期中重复创建。
        if (launchOptions.Role == NetworkRole.Offline || Instance != null)
        {
            return;
        }

        // 这个对象不是场景预制体：运行时创建，并在 Hierarchy 的 DontDestroyOnLoad 场景下可见。
        GameObject bootstrapObject = new GameObject("NetworkBootstrap");
        NetworkBootstrap bootstrap = bootstrapObject.AddComponent<NetworkBootstrap>();
        bootstrap.options = launchOptions;
    }

    private void Awake()
    {
        // 保证每个进程始终只有一个网络总控对象。
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 记录全局实例，之后 LoginPanelController 可通过 Instance 发起游戏服务器连接。
        Instance = this;
        // 登录场景切换到 Main 后，TCP 连接、身份和网络组件仍须保留。
        DontDestroyOnLoad(gameObject);
        // 正常路径中 options 已由 CreateFromCommandLine 注入；此处是 Unity 特殊生命周期下的兜底解析。
        options ??= ParseCommandLine(Environment.GetCommandLineArgs());
        // 清除上一次会话可能残留的玩家、实体、对局和 Tick 数据。
        NetworkRuntime.ResetSession();
        // 将本进程角色写入全局运行时状态，其他网络组件据此选择行为。
        NetworkRuntime.Role = options.Role;

        // 失去窗口焦点时仍让 Unity 主循环运行，否则无法消费网络队列、推进服务器 Tick 或更新远程玩家表现。
        if (!NetworkRuntime.IsOffline)
        {
            // 即使窗口失焦，也继续执行 Update 和网络同步。
            Application.runInBackground = true;
        }

        // 调试面板和本对象一样常驻，用于显示当前角色、连接状态、实体数和 Tick。
        gameObject.AddComponent<NetworkDebugPanel>();
        // 每次场景加载完成后，按当前网络角色关闭单机逻辑并装配网络表现。
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private async void Start()
    {
        // Offline 不启动监听、TCP 连接或网络场景装配。
        if (options.Role == NetworkRole.Offline)
        {
            return;
        }

        try
        {
            if (options.Role == NetworkRole.Server)
            {
                // 服务器不经过登录界面：先监听端口，再加载 Main 作为权威世界。
                StartServer();
                await LoadGameplaySceneAsync();
            }
            else
            {
                // 客户端先停在登录场景，LoginPanelController 登录成功后会调用 ConnectAuthenticatedClientAsync。
                NetworkLog.Info("网络客户端等待登录成功后再连接游戏服务器。");
            }
        }
        catch (Exception exception)
        {
            // 启动阶段不让异常逃出 Unity 生命周期函数，改为写入网络日志。
            NetworkLog.Error($"网络启动失败：{exception}");
        }
    }

    private void Update()
    {
        // 将后台网络线程积累的日志转交到 Unity Console；不处理网络协议本身。
        NetworkLog.FlushToUnityConsole();
    }

    private void OnApplicationQuit()
    {
        // 应用退出时主动释放 socket 和监听端口。
        Shutdown();
    }

    private void OnDestroy()
    {
        // 被销毁的不是当前全局实例时，不应关闭真正仍在运行的网络总控。
        if (Instance != this)
        {
            return;
        }

        // 取消场景回调，避免对象销毁后仍被调用。
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        // 断开客户端连接或停止服务器监听。
        Shutdown();
        // 清除静态入口，允许之后重新创建网络总控。
        Instance = null;
        // 恢复默认角色，避免其他遗留组件误以为仍处于网络模式。
        NetworkRuntime.Role = NetworkRole.Offline;
        // 清除本次会话的玩家、对局和 Tick 状态。
        NetworkRuntime.ResetSession();
    }

    public async Task<WelcomeMessage> ConnectAuthenticatedClientAsync(string playerName, CancellationToken cancellationToken)
    {
        // 这是 LoginPanelController 在 HTTP 登录成功后调用的桥梁：游戏 TCP 连接成功前不能加载 Main。
        // 防止 Server 或 Offline 误调用客户端专属连接流程。
        if (options.Role != NetworkRole.Client)
        {
            throw new InvalidOperationException("只有 Client 模式可以连接游戏服务器。");
        }

        // 只有 HTTP 登录已经建立内存会话后，才允许连接游戏服务器。
        if (!GameSession.IsAuthenticated)
        {
            throw new InvalidOperationException("必须先完成账号登录，才能连接游戏服务器。");
        }

        // 已完成 Welcome 时复用现有连接，避免重复创建 TCP 线程和重复占用玩家席位。
        if (Client != null && Client.IsWelcomed)
        {
            return Client.LastWelcome;
        }

        // GameNetworkClient 挂在 NetworkBootstrap 上，而不是玩家对象上；它负责后台 TCP 收发和主线程事件分发。
        Client ??= gameObject.AddComponent<GameNetworkClient>();
        // 将 GameNetworkClient 的事件转换为可 await 的 Task，供登录流程按顺序等待。
        TaskCompletionSource<WelcomeMessage> completion = new TaskCompletionSource<WelcomeMessage>();
        // 收到 Welcome 代表服务器已分配 PlayerId、EntityId 和 MatchId，连接任务可以成功结束。
        void HandleWelcomed(WelcomeMessage welcome) => completion.TrySetResult(welcome);
        // 连接失败或服务器关闭时，将等待中的任务转成可由登录界面显示的网络异常。
        void HandleFailure(string reason) => completion.TrySetException(
            new NetworkException(NetworkErrorKind.Connection, reason)
        );

        // 临时订阅成功事件。
        Client.Welcomed += HandleWelcomed;
        // 临时订阅失败事件。
        Client.ConnectionFailed += HandleFailure;

        // 登录界面销毁或用户取消时，让 await 立即结束，避免后台连接继续阻塞流程。
        using (cancellationToken.Register(() => completion.TrySetCanceled()))
        {
            try
            {
                // 登录昵称优先，其次使用命令行 -playerName，最后退回到 PlayerId 生成的默认名称。
                string effectivePlayerName = string.IsNullOrWhiteSpace(playerName)
                    ? string.IsNullOrWhiteSpace(options.PlayerName) ? $"Player {options.PlayerId}" : options.PlayerName
                    : playerName;
                // 启动 GameNetworkClient 的后台 TCP 线程；该调用本身不会等待服务器响应。
                Client.Connect(
                    options.ServerAddress,
                    options.ServerPort,
                    options.PlayerId,
                    effectivePlayerName,
                    Application.version
                );
                // 在收到 Welcome 或连接失败事件前保持 await；Welcome 会写入本地 PlayerId、EntityId 和 MatchId。
                return await completion.Task;
            }
            catch (TaskCanceledException exception)
            {
                // 取消等待时关闭正在建立或已建立的 TCP 连接。
                Client.Disconnect();
                // 转换为项目统一的“主动取消”网络异常。
                throw new NetworkException(NetworkErrorKind.Cancelled, "连接游戏服务器的操作已取消。", innerException: exception);
            }
            finally
            {
                // 无论成功、失败或取消，都移除临时事件，避免下次登录重复回调。
                Client.Welcomed -= HandleWelcomed;
                Client.ConnectionFailed -= HandleFailure;
            }
        }
    }

    private void StartServer()
    {
        // 两个服务器组件都附加到常驻 NetworkBootstrap：一个处理 TCP/时钟，一个维护权威玩家状态。
        // 添加 TCP 监听、收包队列和服务器 Tick 驱动组件。
        Server = gameObject.AddComponent<GameNetworkServer>();
        // 添加权威玩家状态管理组件。
        ServerPlayers = gameObject.AddComponent<ServerPlayerManager>();
        // 注册实体；各系统不自行订阅 Tick，由 ServerSimulationLoop 统一安排阶段。
        ServerEntities = gameObject.AddComponent<ServerEntityRegistry>();
        ServerEntities.Initialize(Server);
        // 战斗状态机只在服务器运行，并显式维护波次生成完成与存活计数。
        ServerBattle = gameObject.AddComponent<ServerBattleFlow>();
        ServerBattle.Initialize(Server, ServerEntities);
        // 本 Tick 开火后推进子弹，最后才发送快照。
        ServerProjectiles = gameObject.AddComponent<ServerProjectileRegistry>();
        // 订阅认证、输入和断线事件。
        ServerPlayers.Initialize(Server, ServerEntities, ServerProjectiles, ServerBattle);
        ServerProjectiles.Initialize(Server, ServerEntities);
        // 通用敌人 AI 通过玩家管理器查找最近的存活权威玩家，不在客户端选择目标。
        ServerEntities.SetPlayerManager(ServerPlayers);
        gameObject.AddComponent<ServerSimulationLoop>().Initialize(Server, ServerPlayers, ServerEntities, ServerProjectiles, ServerBattle);
        // 开始监听命令行指定端口。
        Server.StartServer(options.ServerPort);
    }

    private async Task LoadGameplaySceneAsync()
    {
        // 读取当前活动场景名称，避免已经在 Main 时再次加载同一场景。
        if (SceneManager.GetActiveScene().name == "Main")
        {
            // 测试时若已经位于 Main，不重复加载，只补齐网络角色对应的场景装配。
            ApplyRoleToScene();
            SetupGameplayForLoadedScene();
            return;
        }

        // Server 会在这里加载权威世界；Client 则由 LoginPanelController 在 Welcome 后加载相同地址。
        NetworkLog.Info($"正在加载网络游戏场景 {NetworkRuntime.DefaultGameSceneAddress}。");
        // 通过 Addressables 异步加载网络游戏场景，并使用 Single 卸载当前场景。
        AsyncOperationHandle<SceneInstance> handle = Addressables.LoadSceneAsync(
            NetworkRuntime.DefaultGameSceneAddress,
            LoadSceneMode.Single,
            true
        );
        // 等待 Addressables 加载真正结束，不阻塞 Unity 主线程。
        await handle.Task;

        // Addressables 任务完成不必然表示成功，需要检查最终状态。
        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            throw new InvalidOperationException($"无法加载网络游戏场景 {NetworkRuntime.DefaultGameSceneAddress}。");
        }

        // 场景已经存在后，按服务器或客户端角色关闭不应运行的单机组件。
        ApplyRoleToScene();
        // 创建服务器玩家模板，或创建客户端实体注册表和输入发送器。
        SetupGameplayForLoadedScene();
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // 无论 Main 是由服务器启动流程还是登录流程加载，都会在这里再次确保场景角色和网络组件已正确装配。
        if (!NetworkRuntime.IsOffline)
        {
            // 关闭本角色不应运行的原单机组件。
            ApplyRoleToScene();
            // 连接场景对象与网络组件。
            SetupGameplayForLoadedScene();
        }
    }

    private static void ApplyRoleToScene()
    {
        // Server 和 Client 的场景职责不同，因此禁用的组件集合不同。
        if (NetworkRuntime.IsServer)
        {
            // 服务器只计算权威世界，不需要画面、声音、UI 或原本单机玩家脚本。
            // 无图形服务器不需要任何摄像机。
            DisableAll<Camera>();
            // 无图形服务器不需要音频监听器。
            DisableAll<AudioListener>();
            // 无图形服务器不需要 UI 画布。
            DisableAll<Canvas>();
            // 无图形服务器不需要 UI 事件系统。
            DisableAll<EventSystem>();
            // 禁用单机控制器，服务器改由 ServerPlayerManager 根据网络输入移动玩家。
            DisableAll<GrayboxPlayerController>();
            // 禁用单机攻击逻辑，后续应由服务器权威的攻击系统替代。
            DisableAll<GamePlayerAttack>();
            // 禁用单机技能按键读取。
            DisableAll<PlayerSkillInput>();
            // 网络服务器使用 ServerBattleFlow；禁止场景里的单机 WaveManager/EnemySpawner 同时刷怪。
            DisableAll<WaveManager>();
            DisableAll<EnemySpawner>();
            return;
        }

        if (NetworkRuntime.IsClient)
        {
            // 客户端只表现服务器下发的世界快照；不能继续运行本地 Wave、刷怪或单机玩家控制逻辑。
            // 客户端不能自行推进波次。
            DisableAll<WaveManager>();
            // 客户端不能自行刷怪。
            DisableAll<EnemySpawner>();
            // 客户端不能用原控制器直接改本地 Transform。
            DisableAll<GrayboxPlayerController>();
            // 客户端不能用原攻击逻辑直接判伤。
            DisableAll<GamePlayerAttack>();
            // 客户端不能用原技能输入直接改变游戏状态。
            DisableAll<PlayerSkillInput>();
            // 敌人的寻路、选目标和攻击决策只能在服务器运行。
            DisableAll<EnemyAIController>();
            // 客户端敌人位置完全来自服务器快照，不能由本地 NavMeshAgent 再次移动。
            DisableAll<UnityEngine.AI.NavMeshAgent>();
        }
    }

    private void SetupGameplayForLoadedScene()
    {
        // Main 中的 Player_Graybox 既是服务器创建权威玩家的模板，也是客户端创建本地/远程表现对象的模板。
        // Player_Graybox 不存在时没有可复用的玩家模板，当前场景不执行装配。
        if (FindObjectOfType<GrayboxPlayerController>(true) == null)
        {
            return;
        }

        // Server 仅准备模板；玩家实体会在连接认证通过时创建。
        if (NetworkRuntime.IsServer)
        {
            // 准备模板和出生点；实际玩家会在客户端通过 ConnectRequest 验证后创建。
            ServerPlayers?.PrepareScene();
            // 创建服务器权威测试敌人，并开始同步 AI、生命、受击和死亡状态。
            ServerEntities?.PrepareScene();
            // 捕获战斗出生区域，开始等待两名已认证玩家。
            ServerBattle?.PrepareScene();
            return;
        }

        // 仅在“客户端 + 已 Welcome + 尚未创建注册表”时装配一次客户端游戏逻辑。
        if (!NetworkRuntime.IsClient || Client == null || !Client.IsWelcomed || ClientEntities != null)
        {
            return;
        }

        // 客户端必须已经收到 Welcome，才能知道“哪个 EntityId 是我自己”。
        ClientEntities = gameObject.AddComponent<ClientEntityRegistry>();

        // 绑定 Main 中的本地玩家模板，并订阅世界快照事件。
        if (!ClientEntities.Initialize(Client))
        {
            // 初始化失败时移除半成品组件，允许之后场景重载时再次尝试。
            Destroy(ClientEntities);
            // 清空引用，使上面的“尚未创建”条件重新成立。
            ClientEntities = null;
            return;
        }

        ClientBattle = gameObject.AddComponent<ClientBattlePresentation>();
        ClientBattle.Initialize(Client);
        NetworkTransformInterpolator localInterpolator = ClientEntities.LocalPlayerTransform.GetComponent<NetworkTransformInterpolator>();
        ClientPrediction = gameObject.AddComponent<ClientPlayerPrediction>();
        ClientPrediction.Initialize(ClientEntities.LocalPlayerTransform, localInterpolator);
        ClientEntities.SetLocalPrediction(ClientPrediction);
        // 输入发送器按固定 Tick 上传输入，并把相同输入立即交给本地预测器。
        ClientInputSender inputSender = gameObject.AddComponent<ClientInputSender>();
        inputSender.Initialize(Client, ClientEntities.LocalPlayerTransform, ClientPrediction);
    }

    private static void DisableAll<T>() where T : Behaviour
    {
        // 包含 inactive 对象，确保隐藏的单机组件也不会在之后被激活运行。
        T[] behaviours = FindObjectsOfType<T>(true);

        foreach (T behaviour in behaviours)
        {
            // 只停用 Behaviour，不销毁原场景对象；网络表现仍可复用它们的模型和 Transform。
            behaviour.enabled = false;
        }
    }

    private void Shutdown()
    {
        // 已经执行过关闭时直接返回，防止重复 Close/Stop。
        if (shuttingDown)
        {
            return;
        }

        // 先标记关闭状态，防止断线回调或销毁回调再次进入本方法。
        shuttingDown = true;
        // 关闭常驻组件持有的连接和监听端口；场景对象不负责网络资源释放。
        Client?.Disconnect();
        Server?.StopServer();
    }

    private static LaunchOptions ParseCommandLine(string[] args)
    {
        // 支持同一 Build 通过不同参数成为 Server 或 Client，例如：
        // -gameServer -serverPort 7777
        // -gameClient -playerId 1 -serverAddress 127.0.0.1 -serverPort 7777
        // 创建带默认地址和端口的结果；未出现的参数保留默认值。
        LaunchOptions result = new LaunchOptions();

        // 逐项扫描命令行参数。
        for (int i = 0; i < args.Length; i++)
        {
            // 取出当前参数，后续分支决定是否识别它。
            string argument = args[i];

            // -gameServer 使本进程成为服务器。
            if (string.Equals(argument, "-gameServer", StringComparison.OrdinalIgnoreCase))
            {
                // 写入服务器角色。
                result.Role = NetworkRole.Server;
            }
            // -gameClient 使本进程成为客户端。
            else if (string.Equals(argument, "-gameClient", StringComparison.OrdinalIgnoreCase))
            {
                // 写入客户端角色。
                result.Role = NetworkRole.Client;
            }
            // 读取 -serverAddress 后面的服务器地址文本。
            else if (TryReadValue(args, ref i, "-serverAddress", out string address))
            {
                // 覆盖默认服务器地址。
                result.ServerAddress = address;
            }
            // 读取 -serverPort 后面的端口，并限制在有效 TCP/UDP 端口范围内。
            else if (TryReadInt(args, ref i, "-serverPort", out int port) && port >= 1 && port <= 65535)
            {
                // 覆盖默认端口。
                result.ServerPort = port;
            }
            // 读取 -playerId 后面的玩家席位号。
            else if (TryReadInt(args, ref i, "-playerId", out int playerId))
            {
                // 保存客户端申请的玩家席位。
                result.PlayerId = playerId;
            }
            // 读取 -playerName 后面的显示名称。
            else if (TryReadValue(args, ref i, "-playerName", out string playerName))
            {
                // 保存命令行名称备用。
                result.PlayerName = playerName;
            }
        }

        // 未明确指定 PlayerId 的客户端默认申请 1 号席位，保证基本启动参数可用。
        if (result.Role == NetworkRole.Client && result.PlayerId == 0)
        {
            // 写入默认席位号。
            result.PlayerId = 1;
        }

        // 返回完整启动配置。
        return result;
    }

    private static bool TryReadInt(string[] args, ref int index, string name, out int value)
    {
        // 先给 out 参数一个确定初值，失败时调用者不会拿到未初始化数据。
        value = 0;

        // 复用字符串参数读取逻辑；没有该参数或参数后没有值时读取失败。
        if (!TryReadValue(args, ref index, name, out string text))
        {
            return false;
        }

        // 将文本转换成整数；格式非法时返回 false。
        return int.TryParse(text, out value);
    }

    private static bool TryReadValue(string[] args, ref int index, string name, out string value)
    {
        // 先给 out 参数一个空值，失败时调用者可安全忽略。
        value = null;

        // 当前参数名不匹配，或已经是最后一个参数而没有值时，读取失败。
        if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase) || index + 1 >= args.Length)
        {
            return false;
        }

        // 先递增索引跳到参数值，再把该值返回给调用者。
        value = args[++index];
        // 明确表示读取成功。
        return true;
    }
}
