using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 替换项目中旧版 LevelUpController。
///
/// 玩家升级：
/// PlayerExperience.LeveledUp
/// -> 获取随机升级
/// -> GameEntry.UI 打开 UpgradePanel
/// -> 玩家选择
/// -> PlayerUpgradeSystem 应用升级
/// -> 关闭界面并恢复游戏
/// </summary>
public class LevelUpController : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private PlayerExperience playerExperience;

    [SerializeField]
    private PlayerUpgradeSystem upgradeSystem;

    [Header("Choice")]
    [SerializeField, Min(1)]
    private int choiceCount = 3;

    private int pendingLevelUps;
    private bool isChoosing;

    private float previousTimeScale = 1f;
    private CursorLockMode previousCursorLockMode;
    private bool previousCursorVisible;

    private void Awake()
    {
        if (playerExperience == null)
        {
            playerExperience =
                GetComponent<PlayerExperience>();
        }

        if (upgradeSystem == null)
        {
            upgradeSystem =
                GetComponent<PlayerUpgradeSystem>();
        }
    }

    private void OnEnable()
    {
        GameResultController.GameEnded +=
            HandleGameEnded;

        if (playerExperience != null)
        {
            playerExperience.LeveledUp +=
                HandleLevelUp;
        }
    }

    private void OnDisable()
    {
        GameResultController.GameEnded -=
            HandleGameEnded;

        if (playerExperience != null)
        {
            playerExperience.LeveledUp -=
                HandleLevelUp;
        }

        if (isChoosing)
        {
            CloseUpgradePanel();
            ResumeGame();
        }
    }

    private void HandleLevelUp(int newLevel)
    {
        if (GameResultController.HasGameEnded)
        {
            return;
        }

        pendingLevelUps++;

        if (!isChoosing)
        {
            OpenNextSelection();
        }
    }

    private void OpenNextSelection()
    {
        if (GameResultController.HasGameEnded)
        {
            CancelAllSelections();
            return;
        }

        if (upgradeSystem == null)
        {
            return;
        }

        if (GameEntry.Instance == null)
        {
            Debug.LogError(
                "LevelUpController 找不到 GameEntry。",
                this
            );

            return;
        }

        UIManager uiManager =GameEntry.UI;
            

        if (uiManager == null ||!uiManager.HasConfig(UIType.LevelUp))
        {
            Debug.LogError(
                "UIManager 没有配置 LevelUp / UpgradePanel。",
                this
            );

            return;
        }

        List<UpgradeData> choices =
            upgradeSystem.GetRandomChoices(
                choiceCount
            );

        // 所有升级都已经满级。
        if (choices.Count == 0)
        {
            pendingLevelUps = 0;

            CloseUpgradePanel();
            ResumeGame();

            return;
        }

        if (!isChoosing)
        {
            PauseGame();
        }

        UpgradePanelArgs args =
            new UpgradePanelArgs
            {
                Choices = choices,
                UpgradeSystem = upgradeSystem,
                OnSelected = HandleUpgradeSelected
            };

        uiManager.Open<UpgradePanel>(
            UIType.LevelUp,
            args
        );
    }

    private void HandleUpgradeSelected(
        UpgradeData upgrade)
    {
        if (!upgradeSystem.TryApplyUpgrade(
                upgrade))
        {
            // 选择失败，不消耗升级次数，
            // 重新刷新一组三选一。
            OpenNextSelection();

            return;
        }

        pendingLevelUps--;

        if (pendingLevelUps > 0)
        {
            OpenNextSelection();
            return;
        }

        CloseUpgradePanel();
        ResumeGame();
    }

    private void HandleGameEnded()
    {
        CancelAllSelections();
    }

    private void CancelAllSelections()
    {
        pendingLevelUps = 0;
        CloseUpgradePanel();
        ResumeGame();
    }

    private void PauseGame()
    {
        if (isChoosing)
        {
            return;
        }

        isChoosing = true;

        previousTimeScale =
            Time.timeScale;

        previousCursorLockMode =
            Cursor.lockState;

        previousCursorVisible =
            Cursor.visible;

        Time.timeScale = 0f;

        Cursor.lockState =
            CursorLockMode.None;

        Cursor.visible = true;
    }

    private void ResumeGame()
    {
        if (!isChoosing)
        {
            return;
        }

        Time.timeScale =
            previousTimeScale;

        Cursor.lockState =
            previousCursorLockMode;

        Cursor.visible =
            previousCursorVisible;

        isChoosing = false;
    }

    private void CloseUpgradePanel()
    {
        if (GameEntry.Instance == null)
        {
            return;
        }

        UIManager uiManager =
            GameEntry.UI;

        if (uiManager != null)
        {
            uiManager.Close(
                UIType.LevelUp
            );
        }
    }
}
