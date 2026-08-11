using System.Collections.Generic;
using UnityEngine;

public class LevelUpController : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private PlayerExperience playerExperience;

    [SerializeField]
    private PlayerUpgradeSystem upgradeSystem;

    [SerializeField]
    private LevelUpSelectionUI selectionUI;

    [Header("选择数量")]
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
        if (playerExperience != null)
        {
            playerExperience.LeveledUp +=
                HandleLevelUp;
        }
    }

    private void OnDisable()
    {
        if (playerExperience != null)
        {
            playerExperience.LeveledUp -=
                HandleLevelUp;
        }

        if (isChoosing)
        {
            ResumeGame();
        }
    }

    private void HandleLevelUp(int newLevel)
    {
        pendingLevelUps++;

        if (!isChoosing)
        {
            OpenNextSelection();
        }
    }

    private void OpenNextSelection()
    {
        if (upgradeSystem == null ||selectionUI == null)
        {
            return;
        }

        List<UpgradeData> choices =upgradeSystem.GetRandomChoices(choiceCount);

        /*
         * 所有技能都满级了。
         */
        if (choices.Count == 0)
        {
            pendingLevelUps = 0;

            ResumeGame();

            return;
        }

        if (!isChoosing)
        {
            isChoosing = true;

            previousTimeScale =
                Time.timeScale;

            previousCursorLockMode =
                Cursor.lockState;

            previousCursorVisible =
                Cursor.visible;

            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        selectionUI.Show(choices,upgradeSystem,HandleUpgradeSelected);
        
    }

    /// <summary>
    /// 玩家在升级三选一界面中选择某个技能后执行。
    /// upgrade 就是玩家选中的 UpgradeData，
    /// 例如 Upgrade_RapidFire。
    /// </summary>
    private void HandleUpgradeSelected(UpgradeData upgrade)
    {
        /*
         * 尝试真正应用这个升级。
         *
         * 例如玩家选择：
         * Upgrade_RapidFire
         *
         * 就会进入：
         * PlayerUpgradeSystem.TryApplyUpgrade(Upgrade_RapidFire)
         *
         * TryApplyUpgrade 内部会：
         * 1. 检查技能是否已经满级
         * 2. 遍历 UpgradeData.Effects
         * 3. 调用 PlayerCombatStats.ApplyEffect()
         * 4. 更新技能当前等级
         *
         * 返回 true：
         * 升级成功。
         *
         * 返回 false：
         * 升级失败，例如技能已经满级、引用为空等。
         */
        if (!upgradeSystem.TryApplyUpgrade(upgrade))
        {
            /*
             * 如果这次升级没有成功，
             * 不消耗 pendingLevelUps。
             *
             * 重新打开一次技能选择，
             * 让玩家重新选。
             */
            OpenNextSelection();

            return;
        }

        /*
         * 到这里说明刚才升级成功。
         *
         * pendingLevelUps 表示：
         * “还有多少次升级选择没有处理。”
         *
         * 例如：
         * pendingLevelUps = 1
         *
         * 玩家成功选完一个技能以后：
         *
         * 1 → 0
         */
        pendingLevelUps--;

        /*
         * 如果玩家一次获得了大量经验，
         * 可能连续升了多级。
         *
         * 例如：
         *
         * 玩家一次吃了很多经验：
         *
         * Lv.1 → Lv.2
         * Lv.2 → Lv.3
         *
         * 那么 LeveledUp 会触发两次，
         * pendingLevelUps 最终可能等于 2。
         *
         * 第一次选择技能成功：
         *
         * 2 → 1
         *
         * 此时 pendingLevelUps > 0，
         * 说明还有一次升级奖励没有选。
         */
        if (pendingLevelUps > 0)
        {
            /*
             * 不恢复游戏。
             *
             * 直接重新生成下一轮三选一，
             * 让玩家继续选择第二次升级。
             */
            OpenNextSelection();

            return;
        }

        /*
         * pendingLevelUps 已经等于 0，
         * 说明所有升级选择都处理完了。
         *
         * 关闭升级界面，
         * 恢复 Time.timeScale，
         * 让游戏继续运行。
         */
        ResumeGame();
    }

    private void ResumeGame()
    {
        if (selectionUI != null)
        {
            selectionUI.Hide();
        }

        if (isChoosing)
        {
            Time.timeScale =
                previousTimeScale;

            Cursor.lockState =
                previousCursorLockMode;

            Cursor.visible =
                previousCursorVisible;
        }

        isChoosing = false;
    }
}