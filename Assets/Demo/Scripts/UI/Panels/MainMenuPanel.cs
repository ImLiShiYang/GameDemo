using System;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuPanel : UIBase
{
    private const string LuaModuleName = "UI.MainMenu";

    [Header("Buttons")]
    [SerializeField]
    private Button startButton;

    [SerializeField]
    private Button quitButton;

    public static event Action GameStarted;

    private Action startButtonCallback;
    private Action quitButtonCallback;

    protected override void OnInit()
    {
        CallLua("OnInit", this);
    }

    protected override void OnOpen(object args)
    {
        CallLua("OnOpen", this, args);
    }

    protected override void OnRefresh(object args)
    {
        CallLua("OnRefresh", this, args);
    }

    protected override void OnClose()
    {
        CallLua("OnClose", this);
    }

    private bool CallLua(string functionName, params object[] args)
    {
        if (GameEntry.Instance == null)
        {
            Debug.LogError(
                $"主菜单无法调用 Lua：场景中没有 GameEntry。函数={functionName}",
                this
            );
            return false;
        }

        LuaManager luaManager = GameEntry.Lua;

        return luaManager != null && luaManager.Call(LuaModuleName, functionName, args);
    }

    /// <summary>
    /// Lua 在 OnInit 中通过此桥接绑定开始按钮。
    /// </summary>
    public void BindStartButton(Action callback)
    {
        if (startButton != null)
        {
            startButton.onClick.RemoveListener(InvokeStartButtonCallback);
        }

        startButtonCallback = callback;

        if (startButton != null && startButtonCallback != null)
        {
            startButton.onClick.AddListener(InvokeStartButtonCallback);
        }
    }

    /// <summary>
    /// Lua 在 OnInit 中通过此桥接绑定退出按钮。
    /// </summary>
    public void BindQuitButton(Action callback)
    {
        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(InvokeQuitButtonCallback);
        }

        quitButtonCallback = callback;

        if (quitButton != null && quitButtonCallback != null)
        {
            quitButton.onClick.AddListener(InvokeQuitButtonCallback);
        }
    }

    private void InvokeStartButtonCallback()
    {
        InvokeLuaButtonCallback(startButtonCallback, "OnStartClicked");
    }

    private void InvokeQuitButtonCallback()
    {
        InvokeLuaButtonCallback(quitButtonCallback, "OnQuitClicked");
    }

    private void InvokeLuaButtonCallback(Action callback, string functionName)
    {
        if (callback == null)
        {
            Debug.LogError($"主菜单 Lua 按钮未绑定：{functionName}", this);
            return;
        }

        try
        {
            callback.Invoke();
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"调用主菜单 Lua 按钮失败：{LuaModuleName}.{functionName}\n{exception}",
                this
            );
        }
    }

    /// <summary>
    /// Lua 决定关闭主菜单时调用的最小 UI 桥接接口。
    /// </summary>
    public void CloseFromLua()
    {
        CloseSelf();
    }

    /// <summary>
    /// Lua 完成主菜单流程后通知 C# 游戏模块。
    /// </summary>
    public void NotifyGameStartedFromLua()
    {
        GameStarted?.Invoke();
    }

    /// <summary>
    /// 平台相关的退出 API 保留为 Lua 可调用的原子桥接接口。
    /// </summary>
    public void QuitApplicationFromLua()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying =
            false;
#else
        Application.Quit();
#endif
    }

    private void OnDestroy()
    {
        CallLua("OnPanelDestroyed", this);

        if (startButton != null)
        {
            startButton.onClick.RemoveListener(InvokeStartButtonCallback);
        }

        if (quitButton != null)
        {
            quitButton.onClick.RemoveListener(InvokeQuitButtonCallback);
        }

        startButtonCallback = null;
        quitButtonCallback = null;
    }
}
