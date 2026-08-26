using UnityEngine;
using UnityEngine.UI;

public class PausePanel : UIBase
{
    private const string MainSceneAddress = "Scene/Main";
    private const string LoginSceneName = "LoginScene";

    [Header("Buttons")]
    [SerializeField]
    private Button resumeButton;

    [SerializeField]
    private Button restartButton;

    [SerializeField]
    private Button mainMenuButton;

    [SerializeField]
    private Button quitButton;
    
    [SerializeField]
    private Button tutorialButton;
    
    [SerializeField] 
    private Button settingsButton;
    
    [SerializeField] 
    private GameObject windowRoot;
    
    private float previousTimeScale = 1f;

    private CursorLockMode previousCursorLockMode;
    private bool previousCursorVisible;

    protected override void OnInit()
    {
        if (resumeButton != null)
        {
            resumeButton.onClick.AddListener(Resume);
        }

        if (restartButton != null)
        {
            restartButton.onClick.AddListener(RestartCurrentScene);
        }

        if (mainMenuButton != null)
        {
            mainMenuButton.onClick.AddListener(BackToMainMenu);
        }
        
        if (settingsButton != null)
        {
            settingsButton.onClick.AddListener(OpenSettings);
        }
        
        if (tutorialButton != null)
        {
            tutorialButton.onClick.AddListener(OpenTutorial);
        }

        if (quitButton != null)
        {
            quitButton.onClick.AddListener(QuitGame);
        }
    }

    private void OpenSettings()
    {
        if (GameEntry.Instance == null)
        {
            return;
        }

        UIManager uiManager = GameEntry.UI;

        if (uiManager == null || !uiManager.HasConfig(UIType.Settings))
        {
            return;
        }

        // 只隐藏暂停页面的窗口，不关闭 PausePanel。
        // 因此游戏时间和音乐仍然保持暂停。
        if (windowRoot != null)
        {
            windowRoot.SetActive(false);
        }

        uiManager.Open(
            UIType.Settings,
            new SettingsOpenArgs
            {
                OnClosed = ShowPauseWindow
            }
        );
    }

    private void ShowPauseWindow()
    {
        if (windowRoot != null)
        {
            windowRoot.SetActive(true);
        }
    }
    
    private void OpenTutorial()
    {
        if (GameEntry.Instance == null)
        {
            return;
        }

        UIManager uiManager = GameEntry.UI;

        if (uiManager == null || !uiManager.HasConfig(UIType.Tutorial))
        {
            return;
        }

        CloseSelf();

        uiManager.Open(
            UIType.Tutorial,
            new TutorialOpenArgs
            {
                MarkAsShown = false,
                OnConfirmed = () => uiManager.Open(UIType.Pause)
            }
        );
    }
    
    protected override void OnOpen(object args)
    {
        previousTimeScale = Time.timeScale;

        previousCursorLockMode =
            Cursor.lockState;

        previousCursorVisible =
            Cursor.visible;

        Time.timeScale = 0f;

        Cursor.lockState =
            CursorLockMode.None;

        Cursor.visible = true;
        
        if (windowRoot != null)
        {
            windowRoot.SetActive(true);
        }

        GameAudioManager.Instance?.PauseMusic();
    }

    protected override void OnClose()
    {
        // 恢复暂停前的游戏时间。
        Time.timeScale = previousTimeScale;

        // 忽略暂停菜单中最后一次鼠标位置，
        // 等玩家重新移动鼠标后再更新准心。
        GrayboxPlayerController player =
            FindObjectOfType<GrayboxPlayerController>();

        if (player != null)
        {
            player.WaitForAimMouseMovement();
        }

        // 离开暂停界面后恢复游戏鼠标状态。
        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = false;
        
        GameAudioManager.Instance?.ResumeMusic();
    }

    private void Resume()
    {
        CloseSelf();
    }

    private void RestartCurrentScene()
    {
        Time.timeScale = 1f;
        ReloadMainScene();
    }

    private void BackToMainMenu()
    {
        // LoadScene 之前必须恢复时间，
        // 避免新场景继承 timeScale = 0 的状态。
        Time.timeScale = 1f;

        ReturnToLoginScene();
    }

    private void ReloadMainScene()
    {
        AddressableSceneLoader loader = FindFirstObjectByType<AddressableSceneLoader>();

        if (loader == null)
        {
            Debug.LogError("暂停界面没有找到 AddressableSceneLoader。", this);
            return;
        }

        loader.LoadScene(MainSceneAddress);
    }

    private void ReturnToLoginScene()
    {
        AddressableSceneLoader loader = FindFirstObjectByType<AddressableSceneLoader>();

        if (loader == null)
        {
            Debug.LogError("暂停界面没有找到 AddressableSceneLoader。", this);
            return;
        }

        loader.LoadBuiltInScene(LoginSceneName);
    }

    private void QuitGame()
    {
        // 退出前只恢复系统鼠标，不恢复游戏运行。
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
