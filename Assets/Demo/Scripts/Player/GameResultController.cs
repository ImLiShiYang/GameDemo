using UnityEngine;

public class GameResultController : MonoBehaviour
{
    [SerializeField]
    private Health playerHealth;

    private bool gameEnded;

    private void Awake()
    {
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
            playerHealth.Died -=
                HandlePlayerDied;
        }
    }

    private void HandlePlayerDied()
    {
        ShowDefeat();
    }

    public void ShowDefeat()
    {
        if (gameEnded)
        {
            return;
        }

        gameEnded = true;

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

        ShowResult(
            true,
            "胜利",
            "成功完成本局战斗"
        );
    }

    private void ShowResult(
        bool victory,
        string title,
        string detail)
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