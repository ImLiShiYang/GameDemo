using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 登录界面控制器。
/// 当前使用 Mock 登录，后续只需要替换 AuthenticateAsync 的实现即可接入 HTTP。
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

    [Header("Scene Loading")]
    [SerializeField] private AddressableSceneLoader sceneLoader;
    [SerializeField] private string mainSceneAddress = "Scene/Main";

    [Header("Development Failure Tests")]
    [Tooltip("开启后，任意非空账号密码都会返回登录失败。")]
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

    private void Awake()
    {
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
    }

    private async void HandleLoginClicked()
    {
        if (!hotUpdateReady)
        {
            if (hotUpdateFailed && !isUpdating)
            {
                await InitializeHotUpdateAsync();
            }

            return;
        }

        if (isLoggingIn)
        {
            Debug.LogWarning("登录请求正在进行，已忽略重复点击。", this);
            return;
        }

        string account = accountInput != null ? accountInput.text.Trim() : string.Empty;
        string password = passwordInput != null ? passwordInput.text : string.Empty;

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

        isLoggingIn = true;
        SetLoginButtonInteractable(false);
        SetStatus("正在登录...");

        try
        {
            bool loginSucceeded = await AuthenticateAsync(account, password);

            if (!loginSucceeded)
            {
                SetStatus("Mock 登录失败，请检查测试开关。");
                return;
            }

            AddressableSceneLoader activeSceneLoader = ResolveSceneLoader();

            if (activeSceneLoader == null)
            {
                throw new InvalidOperationException("LoginPanelController 没有找到可用的 AddressableSceneLoader。");
            }

            SetStatus("登录成功，正在异步加载主界面...");

            string targetAddress = simulateSceneLoadFailure ? "Scene/MissingForFailureTest" : mainSceneAddress;
            await activeSceneLoader.LoadSceneAsync(targetAddress);
        }
        catch (Exception exception)
        {
            if (this != null)
            {
                SetStatus("登录或场景加载失败，请查看 Console。 ");
            }

            Debug.LogError($"登录流程发生异常：\n{exception}", this);
        }
        finally
        {
            // 加载成功后 LoginScene 已经卸载，此对象会等同于 null。
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

    private async Task<bool> AuthenticateAsync(string account, string password)
    {
        // 模拟一次网络往返。Day 17 将这里替换为 ILoginService.LoginAsync。
        await Task.Delay(700);

        return !simulateLoginFailure && !string.IsNullOrEmpty(account) && !string.IsNullOrEmpty(password);
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
