using UnityEngine;
using UnityEngine.UI;
using System;

public class MainMenuPanel : UIBase
{
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
                StartGame
            );
        }

        if (quitButton != null)
        {
            quitButton.onClick.AddListener(
                QuitGame
            );
        }
    }

    protected override void OnOpen(object args)
    {
        // 主菜单出现时暂停游戏。
        Time.timeScale = 0f;

        Cursor.lockState =CursorLockMode.None;
        Cursor.visible = true;
    }

    private void StartGame()
    {
        Time.timeScale = 1f;

        CloseSelf();

        GameStarted?.Invoke();
    }

    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying =
            false;
#else
        Application.Quit();
#endif
    }
}