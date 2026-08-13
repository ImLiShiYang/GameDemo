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

    protected override void OnInit()
    {
        if (startButton != null)
        {
            startButton.onClick.AddListener(
                HandleStartButtonClicked
            );
        }

        if (quitButton != null)
        {
            quitButton.onClick.AddListener(
                HandleQuitButtonClicked
            );
        }
    }

    protected override void OnOpen(object args)
    {
        // 主菜单出现时暂停游戏。
        Time.timeScale = 0f;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void HandleStartButtonClicked()
    {
        if (TryCallLua("OnStartClicked"))
        {
            return;
        }

        StartGameFromLua();
    }

    private void HandleQuitButtonClicked()
    {
        if (TryCallLua("OnQuitClicked"))
        {
            return;
        }

        QuitGameFromLua();
    }

    private bool TryCallLua(string functionName)
    {
        if (GameEntry.Instance == null)
        {
            return false;
        }

        LuaManager luaManager = GameEntry.Lua;

        return luaManager != null && luaManager.Call(LuaModuleName,functionName,this);
    }

    /// <summary>
    /// 提供给 Lua 调用的开始游戏入口。
    /// </summary>
    public void StartGameFromLua()
    {
        Time.timeScale = 1f;

        CloseSelf();

        GameStarted?.Invoke();
    }

    /// <summary>
    /// 提供给 Lua 调用的退出游戏入口。
    /// </summary>
    public void QuitGameFromLua()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying =
            false;
#else
        Application.Quit();
#endif
    }
}