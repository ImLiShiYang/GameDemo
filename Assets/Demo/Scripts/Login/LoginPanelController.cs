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

    [Header("Scene Loading")]
    [SerializeField] private AddressableSceneLoader sceneLoader;
    [SerializeField] private string mainSceneAddress = "Scene/Main";

    [Header("Development Failure Tests")]
    [Tooltip("开启后，任意非空账号密码都会返回登录失败。")]
    [SerializeField] private bool simulateLoginFailure;
    [Tooltip("开启后，登录成功后会故意加载一个不存在的场景地址。")]
    [SerializeField] private bool simulateSceneLoadFailure;

    private bool isLoggingIn;

    private void Awake()
    {
        if (loginButton != null)
        {
            loginButton.onClick.AddListener(HandleLoginClicked);
        }

        SetStatus(string.Empty);
    }

    private void OnDestroy()
    {
        if (loginButton != null)
        {
            loginButton.onClick.RemoveListener(HandleLoginClicked);
        }
    }

    private async void HandleLoginClicked()
    {
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
