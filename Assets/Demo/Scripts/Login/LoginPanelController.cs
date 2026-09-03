using System;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 登录界面控制器。
/// 启动时完成热更新，随后通过 HTTP 登录并拉取玩家数据。
/// </summary>
public sealed class LoginPanelController : MonoBehaviour
{
    [Header("Login UI")]
    [SerializeField] private TMP_InputField accountInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Button loginButton;
    [SerializeField] private TMP_Text statusText;

    [Header("Hot Update")]
    [SerializeField] private bool enableHotUpdate = true;
    [SerializeField] private string hotUpdateBaseUrl = "http://127.0.0.1:8000/HotUpdate/Windows";
    [SerializeField, Min(1)] private int hotUpdateTimeoutSeconds = 10;

    [Header("HTTP Network")]
    [SerializeField] private string apiBaseUrl = "http://127.0.0.1:8080";
    [SerializeField, Min(1)] private int apiTimeoutSeconds = 5;
    [SerializeField, Range(1, 5)] private int apiMaximumAttempts = 3;

    [Header("Scene Loading")]
    [SerializeField] private AddressableSceneLoader sceneLoader;
    [SerializeField] private string mainSceneAddress = "Scene/Main";

    [Header("Development Failure Tests")]
    [Tooltip("开启后会向 Mock 服务发送错误密码，用于验证 HTTP 401。")]
    [SerializeField] private bool simulateLoginFailure;
    [Tooltip("开启后，登录成功后会故意加载一个不存在的场景地址。")]
    [SerializeField] private bool simulateSceneLoadFailure;

    private bool isLoggingIn;
    // 当前是否正在更新
    private bool isUpdating;
    // 是否已经有一套可运行内容
    private bool hotUpdateReady;
    // 上一次是否失败
    private bool hotUpdateFailed;
    private HotUpdateManager hotUpdateManager;
    private IJsonSerializer jsonSerializer;
    private UnityWebRequestHttpClient httpClient;
    private ILoginService loginService;
    private IPlayerService playerService;
    private CancellationTokenSource lifetimeCancellation;

    private void Awake()
    {
        lifetimeCancellation = new CancellationTokenSource();

        if (loginButton != null)
        {
            loginButton.onClick.AddListener(HandleLoginClicked);
        }

        //先禁用登录按钮，防止玩家在更新完成前进入主场景。
        SetLoginButtonInteractable(false);
        SetStatus("准备检查更新...");
    }

    private async void Start()
    {
        await InitializeHotUpdateAsync();
    }

    private void OnDestroy()
    {
        if (loginButton != null)
        {
            loginButton.onClick.RemoveListener(HandleLoginClicked);
        }

        if (hotUpdateManager != null)
        {
            hotUpdateManager.StatusChanged -= HandleHotUpdateStatusChanged;
            hotUpdateManager.ProgressChanged -= HandleHotUpdateProgressChanged;
        }

        if (httpClient != null)
        {
            httpClient.Retrying -= HandleHttpRetrying;
        }

        lifetimeCancellation?.Cancel();
        lifetimeCancellation?.Dispose();
        lifetimeCancellation = null;
    }

    /// <summary>
    /// 响应登录按钮点击，依次完成热更新门禁、输入校验、HTTP 登录、玩家数据获取和主场景加载。
    /// 这是按钮事件，因此使用 async void；方法内部会捕获并处理所有预期异常，避免异常逃离事件入口。
    /// </summary>
    private async void HandleLoginClicked()
    {
        // Lua 和配置尚未准备好时不允许进入主场景。
        // 如果上一次热更新失败，则本次点击优先作为“重试更新”，不会同时发起登录请求。
        if (!hotUpdateReady)
        {
            if (hotUpdateFailed && !isUpdating)
            {
                await InitializeHotUpdateAsync();
            }

            return;
        }

        // 防止玩家连续点击按钮，产生多条并发登录和场景加载链路。
        if (isLoggingIn)
        {
            Debug.LogWarning("登录请求正在进行，已忽略重复点击。", this);
            return;
        }

        // 从 UI 获取输入。账号去掉首尾空格，密码保留原始内容，避免改变合法密码。
        string account = accountInput != null ? accountInput.text.Trim() : string.Empty;
        string password = passwordInput != null ? passwordInput.text : string.Empty;

        // 在发起网络请求前完成最基本的本地校验，并将输入焦点交还给对应输入框。
        if (string.IsNullOrEmpty(account))
        {
            SetStatus("请输入账号。");
            accountInput?.ActivateInputField();
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            SetStatus("请输入密码。");
            passwordInput?.ActivateInputField();
            return;
        }

        // 标记登录进行中并禁用按钮，直到流程成功切换场景或失败后进入 finally。
        isLoggingIn = true;
        SetLoginButtonInteractable(false);
        SetStatus("正在连接登录服务器...");

        try
        {
            // 清理上一次登录留下的内存会话，避免请求失败后继续使用旧 Token 或旧玩家资料。
            GameSession.Clear();

            // AuthenticateAsync 内部先请求登录接口获取 Token，再携带 Token 请求玩家资料。
            // LoginPanel 销毁时 CancellationToken 会取消仍在执行的网络请求。
            PlayerProfile player = await AuthenticateAsync(account, password, lifetimeCancellation.Token);

            if (NetworkRuntime.IsClient)
            {
                NetworkBootstrap bootstrap = NetworkBootstrap.Instance;

                if (bootstrap == null)
                {
                    throw new InvalidOperationException("Client 模式没有找到 NetworkBootstrap。");
                }

                SetStatus($"登录成功，欢迎 {player.nickname}（Lv.{player.level}），正在连接游戏服务器...");
                await bootstrap.ConnectAuthenticatedClientAsync(player.nickname, lifetimeCancellation.Token);
                SetStatus("游戏服务器验证成功，正在加载主场景...");
            }

            // 登录和玩家数据均成功后，获取负责加载 Addressable 主场景的组件。
            AddressableSceneLoader activeSceneLoader = ResolveSceneLoader();

            if (activeSceneLoader == null)
            {
                throw new InvalidOperationException("LoginPanelController 没有找到可用的 AddressableSceneLoader。");
            }

            if (!NetworkRuntime.IsClient)
            {
                SetStatus($"登录成功，欢迎 {player.nickname}（Lv.{player.level}），正在加载主界面...");
            }

            // 开发测试开关可以故意使用不存在的地址，验证场景加载失败的异常处理。
            string targetAddress = simulateSceneLoadFailure ? "Scene/MissingForFailureTest" : mainSceneAddress;
            await activeSceneLoader.LoadSceneAsync(targetAddress);
        }
        catch (NetworkException exception)
        {
            // 网络层的可预期错误使用统一的用户提示。
            // 场景销毁导致的主动取消属于正常生命周期，不需要显示错误或输出警告。
            if (this != null && exception.Kind != NetworkErrorKind.Cancelled)
            {
                string message = NetworkRuntime.IsClient && GameSession.IsAuthenticated
                    ? exception.Message
                    : GetLoginErrorMessage(exception);
                SetStatus(message);
                Debug.LogWarning($"登录网络请求失败：{exception.Message}\n{exception.ResponseText}", this);
            }
        }
        catch (Exception exception)
        {
            // 捕获网络错误以外的异常，例如服务初始化失败或 Addressable 场景加载失败。
            if (this != null)
            {
                SetStatus("登录或场景加载失败，请查看 Console。 ");
            }

            Debug.LogError($"登录流程发生异常：\n{exception}", this);
        }
        finally
        {
            // 失败时 LoginPanel 仍然存在，需要恢复状态和按钮，让玩家可以再次登录。
            // 成功加载主场景后 LoginScene 已卸载，Unity 中被销毁的组件会与 null 相等，不能再访问其 UI。
            if (this != null)
            {
                isLoggingIn = false;
                SetLoginButtonInteractable(true);
            }
        }
    }

    /// <summary>
    /// 初始化登录界面的热更新流程。
    /// 更新成功或已有可用缓存时允许登录；更新失败时允许玩家点击登录按钮重试。
    /// </summary>
    private async Task InitializeHotUpdateAsync()
    {
        // 防止重复启动热更新。
        // 例如玩家连续点击重试按钮时，只允许第一个更新任务继续执行。
        if (isUpdating)
        {
            return;
        }

        // Inspector 中关闭热更新功能时，直接将内容标记为可用并开放登录按钮。
        if (!enableHotUpdate)
        {
            hotUpdateReady = true;
            hotUpdateFailed = false;
            SetStatus("热更新检查已关闭，可以登录。");
            SetLoginButtonInteractable(true);
            return;
        }

        // 记录当前正在进行热更新，并重置上一次更新留下的状态。
        isUpdating = true;
        hotUpdateReady = false;
        hotUpdateFailed = false;

        // 更新完成之前禁止登录，避免主场景提前加载旧版本的 Lua 和配置。
        SetLoginButtonInteractable(false);

        // 获取当前物体上的 HotUpdateManager。
        // 如果没有该组件，ResolveHotUpdateManager 会动态添加并订阅状态、进度事件。
        ResolveHotUpdateManager();

        try
        {
            // 执行完整的热更新流程，并异步等待最终结果。
            // await 等待期间不会阻塞 Unity 主循环，界面仍然可以正常刷新。
            HotUpdateResult result = await hotUpdateManager.RunAsync(hotUpdateBaseUrl, hotUpdateTimeoutSeconds);

            // await 等待期间可能已经切换场景，当前 LoginPanelController 可能已被销毁。
            // Unity 对 Object 的 == 运算符进行了特殊处理，被销毁的组件与 null 比较时会返回 true。
            // 此时不能再修改该组件上的状态或 UI。
            if (this == null)
            {
                return;
            }

            // RunAsync 返回成功时，说明已经准备好一套可以正常使用的内容。
            // 这个成功结果既可能是最新远端内容，也可能是校验通过的本地缓存内容。
            hotUpdateReady = result.Success;

            // 更新失败状态与成功状态相反。
            // 失败后登录按钮会被重新启用，供玩家点击重试。
            hotUpdateFailed = !result.Success;

            if (result.Success)
            {
                // UsedCachedVersion 为 true 表示本次使用了本地缓存；
                // 为 false 表示已经完成远端检查，并确认或更新到了最新内容。
                string source = result.UsedCachedVersion ? "缓存" : "最新";
                SetStatus($"已准备{source}内容 {result.Version}，可以登录。");
            }
            else
            {
                // 更新没有准备好可用内容时显示失败原因，并提示玩家点击登录按钮重试。
                SetStatus($"更新失败：{result.Message}\n点击登录按钮重试。");
            }
        }
        catch (Exception exception)
        {
            // RunAsync 正常情况下会将内部异常转换成 HotUpdateResult.Failed。
            // 这里是登录界面的最后一层保护，用来捕获未被 RunAsync 处理的意外异常。

            // 如果等待期间当前组件已经被销毁，就不能再访问它的 UI。
            if (this == null)
            {
                return;
            }

            // 标记本次更新失败。
            // hotUpdateReady 在流程开始时已经被设置为 false，因此这里不需要重复设置。
            hotUpdateFailed = true;

            // 提示玩家可以点击登录按钮重新执行热更新。
            SetStatus("更新发生异常，点击登录按钮重试。");

            // 将完整异常和调用栈输出到 Unity Console，方便定位问题。
            Debug.LogError($"初始化热更新失败：\n{exception}", this);
        }
        finally
        {
            // finally 无论成功、失败、中途 return 或出现异常都会执行。
            // 但如果当前组件已经因场景切换被销毁，就不再访问它的字段和 UI。
            if (this != null)
            {
                // 解除“正在更新”状态，使后续可以重新执行更新流程。
                isUpdating = false;

                // 更新成功时启用按钮，让玩家正常登录。
                // 更新失败时也启用按钮，让玩家点击按钮重试。
                // 只有仍在更新且尚无结果时，登录按钮才保持禁用。
                SetLoginButtonInteractable(hotUpdateReady || hotUpdateFailed);
            }
        }
    }

    private void ResolveHotUpdateManager()
    {
        if (hotUpdateManager != null)
        {
            return;
        }

        hotUpdateManager = GetComponent<HotUpdateManager>();

        if (hotUpdateManager == null)
        {
            hotUpdateManager = gameObject.AddComponent<HotUpdateManager>();
        }

        hotUpdateManager.StatusChanged += HandleHotUpdateStatusChanged;
        hotUpdateManager.ProgressChanged += HandleHotUpdateProgressChanged;
    }

    private void HandleHotUpdateStatusChanged(string message)
    {
        SetStatus(message);
    }

    private void HandleHotUpdateProgressChanged(float progress, long downloadedBytes, long totalBytes)
    {
        int percentage = Mathf.RoundToInt(progress * 100f);
        SetStatus($"正在准备更新：{percentage}%（{FormatBytes(downloadedBytes)}/{FormatBytes(totalBytes)}）");
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024f * 1024f):0.00} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024f:0.00} KB";
        }

        return $"{bytes} B";
    }

    /// <summary>
    /// 完成一次完整的身份认证：先登录获取 Token，再使用 Token 拉取玩家资料并建立内存会话。
    /// </summary>
    /// <param name="account">经过登录界面本地校验的账号。</param>
    /// <param name="password">玩家输入的原始密码。</param>
    /// <param name="cancellationToken">LoginPanel 销毁时用于终止仍在进行的网络请求。</param>
    /// <returns>登录成功后取得的玩家资料。</returns>
    private async Task<PlayerProfile> AuthenticateAsync(string account, string password,CancellationToken cancellationToken)
    {
        // 延迟创建网络依赖，只有玩家真正发起登录时才初始化 HTTP 客户端和业务服务。
        ResolveNetworkServices();

        // 开启失败测试时故意替换为错误密码，让 Mock 服务返回真实的 HTTP 401。
        string submittedPassword = simulateLoginFailure ? "__simulate_login_failure__" : password;

        // 第一个请求验证账号密码，并取得后续接口需要的 Token 和玩家 ID。
        LoginResponse login = await loginService.LoginAsync(account, submittedPassword, cancellationToken);

        // await 返回时场景可能已经切换，因此更新界面前先确认当前组件仍然存在。
        if (this != null)
        {
            SetStatus("登录成功，正在获取玩家数据...");
        }

        // 第二个请求携带登录 Token 获取玩家的昵称、等级和经验等基础资料。
        PlayerProfile player = await playerService.GetPlayerAsync(login.playerId, login.token, cancellationToken);

        // 两个请求均成功后再提交会话，避免主场景读取到只有 Token、没有玩家资料的不完整状态。
        GameSession.Set(login, player);
        return player;
    }

    /// <summary>
    /// 按依赖顺序创建 JSON、HTTP、登录和玩家服务，并订阅 HTTP 自动重试事件。
    /// 同一个 LoginPanel 生命周期内只创建一次，后续登录重试会复用这些对象。
    /// </summary>
    private void ResolveNetworkServices()
    {
        // 两个业务服务都已存在时说明依赖已经初始化，无需重复创建或重复订阅事件。
        if (loginService != null && playerService != null)
        {
            return;
        }

        // JSON 序列化器由 HTTP 客户端和登录服务共同使用，保证请求与响应采用一致的格式。
        jsonSerializer = new UnityJsonSerializer();

        // HTTP 客户端集中处理 UnityWebRequest、超时、错误分类和自动重试。
        httpClient = new UnityWebRequestHttpClient(jsonSerializer);

        // 将底层重试状态转发到登录 UI；OnDestroy 中会解除订阅。
        httpClient.Retrying += HandleHttpRetrying;

        // 业务服务只关心各自的接口和 DTO，共享相同的地址、HTTP 客户端及请求策略。
        loginService = new LoginService(apiBaseUrl, httpClient, jsonSerializer, apiTimeoutSeconds, apiMaximumAttempts);
        playerService = new PlayerService(apiBaseUrl, httpClient, apiTimeoutSeconds, apiMaximumAttempts);
    }

    /// <summary>
    /// 接收 HTTP 客户端的自动重试通知，将次数、等待时间和失败原因输出到 Console 与登录界面。
    /// </summary>
    private void HandleHttpRetrying(HttpRetryInfo retryInfo)
    {
        // 网络请求等待期间 LoginScene 可能已经卸载，被销毁的组件不能继续访问 UI。
        if (this == null)
        {
            return;
        }

        // Console 保留完整 URL 和底层原因，方便开发阶段定位具体失败接口。
        Debug.LogWarning(
            $"HTTP 请求即将重试：{retryInfo.NextAttempt}/{retryInfo.MaximumAttempts}，" +
            $"URL={retryInfo.Url}，原因={retryInfo.Error.Message}",
            this
        );

        // 玩家界面只显示必要的重试进度，不暴露底层异常和接口地址。
        SetStatus($"网络请求失败，{retryInfo.DelaySeconds:0.0} 秒后进行第 " +
                  $"{retryInfo.NextAttempt}/{retryInfo.MaximumAttempts} 次尝试...");
    }

    /// <summary>
    /// 将网络层异常转换成适合直接展示给玩家的登录提示。
    /// 优先处理登录特有错误，其次采用服务端错误消息，最后回退到统一网络错误文案。
    /// </summary>
    private string GetLoginErrorMessage(NetworkException exception)
    {
        // 401 在登录场景中表示身份凭据未通过，不显示通用的“身份验证失败”。
        if (exception.StatusCode == 401)
        {
            return "账号或密码错误，请重新输入。";
        }

        // 其他 HTTP 错误可能携带 Mock 或正式服务端返回的结构化错误信息。
        if (exception.Kind == NetworkErrorKind.Http && !string.IsNullOrWhiteSpace(exception.ResponseText))
        {
            try
            {
                ApiErrorResponse error = jsonSerializer?.Deserialize<ApiErrorResponse>(exception.ResponseText);

                // 服务端提供了明确文案时优先展示，例如限流、维护或权限提示。
                if (!string.IsNullOrWhiteSpace(error?.message))
                {
                    return error.message;
                }
            }
            catch (NetworkException)
            {
                // 错误响应本身不是合法 JSON 时，使用统一网络错误文案。
            }
        }

        // 连接失败、超时、序列化失败等情况使用 NetworkException 内置的统一用户文案。
        return exception.GetUserMessage();
    }

    private AddressableSceneLoader ResolveSceneLoader()
    {
        if (sceneLoader != null)
        {
            return sceneLoader;
        }

        if (AddressableResourceManager.Instance != null)
        {
            sceneLoader = AddressableResourceManager.Instance.GetComponent<AddressableSceneLoader>();
        }

        if (sceneLoader == null)
        {
            sceneLoader = FindFirstObjectByType<AddressableSceneLoader>();
        }

        return sceneLoader;
    }

    private void SetLoginButtonInteractable(bool interactable)
    {
        if (loginButton != null)
        {
            loginButton.interactable = interactable;
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
}
