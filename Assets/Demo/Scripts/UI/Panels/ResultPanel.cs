using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ResultOpenArgs
{
    public bool Victory;

    public string Title;

    public string Detail;
}

public class ResultPanel : UIBase
{
    [Header("Text")]
    [SerializeField]
    private TMP_Text titleText;

    [SerializeField]
    private TMP_Text detailText;

    [Header("Buttons")]
    [SerializeField]
    private Button restartButton;

    [SerializeField]
    private Button mainMenuButton;

    private float previousTimeScale = 1f;

    protected override void OnInit()
    {
        if (restartButton != null)
        {
            restartButton.onClick.AddListener(
                RestartGame
            );
        }

        if (mainMenuButton != null)
        {
            mainMenuButton.onClick.AddListener(
                BackToMainMenu
            );
        }
    }

    protected override void OnOpen(object args)
    {
        previousTimeScale =
            Time.timeScale;

        // Keep gameplay time running while the result screen is open.

        Cursor.lockState =
            CursorLockMode.None;

        Cursor.visible = true;

        RefreshContent(args);
    }

    protected override void OnRefresh(
        object args)
    {
        RefreshContent(args);
    }

    protected override void OnClose()
    {
        Time.timeScale =
            previousTimeScale;
    }

    private void RefreshContent(
        object args)
    {
        ResultOpenArgs result =
            args as ResultOpenArgs;

        if (result == null)
        {
            if (titleText != null)
            {
                titleText.text =
                    "游戏结束";
            }

            if (detailText != null)
            {
                detailText.text =
                    string.Empty;
            }

            return;
        }

        if (titleText != null)
        {
            if (!string.IsNullOrWhiteSpace(
                    result.Title))
            {
                titleText.text =
                    result.Title;
            }
            else
            {
                titleText.text =
                    result.Victory
                        ? "胜利"
                        : "战斗失败";
            }
        }

        if (detailText != null)
        {
            detailText.text =
                result.Detail ?? string.Empty;
        }
    }

    private void RestartGame()
    {
        Time.timeScale = 1f;

        // 下一次场景加载后直接开始战斗。
        GameUIBootstrap.StartGameDirectlyOnNextLoad();

        Scene scene =
            SceneManager.GetActiveScene();

        SceneManager.LoadScene(
            scene.buildIndex
        );
    }

    private void BackToMainMenu()
    {
        Time.timeScale = 1f;

        // 正常重新加载场景，
        // GameUIBootstrap 会显示主菜单。
        Scene scene =
            SceneManager.GetActiveScene();

        SceneManager.LoadScene(
            scene.buildIndex
        );
    }
}