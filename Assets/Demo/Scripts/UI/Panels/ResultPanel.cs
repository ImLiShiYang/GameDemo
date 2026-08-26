using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class ResultOpenArgs
{
    public bool Victory;

    public string Title;

    public string Detail;

    public ScoreResult Score;

    public int HighestScore;
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

        // 结算页面打开后冻结战斗，玩家、敌人和弹道都停止更新。

        Time.timeScale = 0f;

        Cursor.lockState =
            CursorLockMode.None;

        Cursor.visible = true;

        RefreshContent(args);
    }

    protected override void OnRefresh(
        object args)
    {
        Time.timeScale = 0f;
        RefreshContent(args);
    }

    protected override void OnClose()
    {
        Time.timeScale =
            previousTimeScale;
    }

    private void RefreshContent(object args)
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
            detailText.text = BuildDetail(result);
        }
    }

    private static string BuildDetail(ResultOpenArgs result)
    {
        string detail = result.Detail ?? string.Empty;

        if (!result.Victory || result.Score == null)
        {
            return $"{detail}\n\n历史最高分：{result.HighestScore}";
        }

        ScoreResult score = result.Score;
        string newRecordText = score.IsNewHighestScore ? "\n新纪录！" : string.Empty;

        return
            $"{detail}\n\n" +
            $"通关基础分：{score.ClearBaseScore}\n" +
            $"时间奖励：{score.TimeBonus}（用时 {FormatTime(score.ClearTime)}）\n" +
            $"生命奖励：{score.HealthBonus}（剩余 {Mathf.RoundToInt(score.RemainingHealthRatio * 100f)}%）\n" +
            $"最终得分：{score.FinalScore}\n" +
            $"历史最高分：{score.HighestScore}" +
            newRecordText;
    }

    private static string FormatTime(float seconds)
    {
        int totalSeconds = Mathf.Max(0, Mathf.FloorToInt(seconds));
        int minutes = totalSeconds / 60;
        int remainingSeconds = totalSeconds % 60;
        return $"{minutes:00}:{remainingSeconds:00}";
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
