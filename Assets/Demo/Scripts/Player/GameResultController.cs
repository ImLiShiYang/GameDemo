using System;
using System.Collections;
using UnityEngine;

public class GameResultController : MonoBehaviour
{
    public static event Action GameEnded;

    public static bool HasGameEnded { get; private set; }

    [SerializeField]
    private Health playerHealth;

    [Header("Timing")]
    [SerializeField, Min(0f)]
    [Tooltip("玩家死亡后，显示失败结算前的停留时间。")]
    private float defeatDelay = 3f;

    private bool gameEnded;
    private bool resultPending;
    private WaveManager waveManager;

    private void Awake()
    {
        HasGameEnded = false;

        if (playerHealth == null)
        {
            GrayboxPlayerController player =
                FindObjectOfType<
                    GrayboxPlayerController
                >();

            if (player != null)
            {
                playerHealth =
                    player.GetComponent<Health>();
            }
        }
    }

    private void Start()
    {
        waveManager = GameEntry.Wave;

        if (waveManager != null)
        {
            waveManager.Victory +=HandleVictory;
        }
    }

    private void OnEnable()
    {
        if (playerHealth != null)
        {
            playerHealth.Died +=
                HandlePlayerDied;
        }
    }

    private void OnDisable()
    {
        if (playerHealth != null)
        {
            playerHealth.Died -=HandlePlayerDied;
        }

        if (waveManager != null)
        {
            waveManager.Victory -=HandleVictory;
        }
    }

    private void HandlePlayerDied()
    {
        if (gameEnded || resultPending)
        {
            return;
        }

        resultPending = true;
        StartCoroutine(ShowDefeatAfterDelay());
    }

    private void HandleVictory()
    {
        if (gameEnded || resultPending)
        {
            return;
        }

        ShowVictory();
    }

    private IEnumerator ShowDefeatAfterDelay()
    {
        if (defeatDelay > 0f)
        {
            yield return new WaitForSeconds(defeatDelay);
        }

        ShowDefeat();
    }

    public void ShowDefeat()
    {
        if (gameEnded)
        {
            return;
        }

        gameEnded = true;
        resultPending = false;
        HasGameEnded = true;
        GameEnded?.Invoke();

        ShowResult(
            false,
            "战斗失败",
            "你被敌人击败了"
        );
    }

    public void ShowVictory()
    {
        if (gameEnded)
        {
            return;
        }

        gameEnded = true;
        resultPending = false;
        HasGameEnded = true;
        GameEnded?.Invoke();

        ShowResult(
            true,
            "胜利",
            "成功完成本局战斗"
        );
    }

    private void ShowResult(bool victory,string title,string detail)
    {
        ScoreManager scoreManager = GameEntry.Score;
        ScoreResult scoreResult = victory && scoreManager != null
            ? scoreManager.CompleteVictory(playerHealth)
            : null;

        UIManager uiManager =
            GameEntry.UI;

        if (uiManager == null)
        {
            return;
        }

        // 战斗HUD可以先关掉。
        uiManager.Close(
            UIType.HUD
        );

        // 防止结算时还留着暂停或升级界面。
        uiManager.Close(
            UIType.Pause
        );

        uiManager.Close(
            UIType.LevelUp
        );

        ResultOpenArgs args =
            new ResultOpenArgs
            {
                Victory = victory,
                Title = title,
                Detail = detail,
                Score = scoreResult,
                HighestScore = scoreResult != null
                    ? scoreResult.HighestScore
                    : scoreManager != null ? scoreManager.HighestScore : 0
            };

        uiManager.Open<ResultPanel>(
            UIType.Result,
            args
        );
    }
}
