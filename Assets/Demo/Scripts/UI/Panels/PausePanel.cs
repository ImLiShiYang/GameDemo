using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class PausePanel : UIBase
{
    [Header("Buttons")]
    [SerializeField]
    private Button resumeButton;

    [SerializeField]
    private Button restartButton;

    [SerializeField]
    private Button mainMenuButton;

    [SerializeField]
    private Button quitButton;
    

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

        if (quitButton != null)
        {
            quitButton.onClick.AddListener(QuitGame);
        }
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
    }

    protected override void OnClose()
    {
        Time.timeScale =
            previousTimeScale;

        Cursor.lockState =
            previousCursorLockMode;

        Cursor.visible =
            previousCursorVisible;
    }

    private void Resume()
    {
        CloseSelf();
    }

    private void RestartCurrentScene()
    {
        Time.timeScale = 1f;

        Scene activeScene =
            SceneManager.GetActiveScene();

        SceneManager.LoadScene(
            activeScene.buildIndex
        );
    }

    private void BackToMainMenu()
    {
        // LoadScene 之前必须恢复时间，
        // 避免新场景继承 timeScale = 0 的状态。
        Time.timeScale = 1f;

        Scene currentScene =
            SceneManager.GetActiveScene();

        SceneManager.LoadScene(
            currentScene.buildIndex
        );
    }

    private void QuitGame()
    {
        Time.timeScale = 1f;

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
