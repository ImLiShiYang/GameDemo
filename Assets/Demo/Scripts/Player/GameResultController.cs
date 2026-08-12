using System;
using UnityEngine;

public class GameResultController : MonoBehaviour
{
    public static event Action GameEnded;

    public static bool HasGameEnded { get; private set; }

    [SerializeField]
    private Health playerHealth;

    private bool gameEnded;
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
        ShowDefeat();
    }

    private void HandleVictory()
    {
        ShowVictory();
    }

    public void ShowDefeat()
    {
        if (gameEnded)
        {
            return;
        }

        gameEnded = true;
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
                Detail = detail
            };

        uiManager.Open<ResultPanel>(
            UIType.Result,
            args
        );
    }
}