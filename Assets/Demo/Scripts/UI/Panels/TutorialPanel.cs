using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TutorialPanel : UIBase
{
    public const string TutorialPlayerPrefsKey =
        "GameDemo_TutorialShown";

    public static bool HasBeenShown =>
        PlayerPrefs.GetInt(
            TutorialPlayerPrefsKey,
            0
        ) == 1;
    
    [SerializeField]
    private TMP_Text tutorialText;

    [SerializeField]
    private Button confirmButton;

    [SerializeField]
    private bool pauseGame = true;

    private float previousTimeScale = 1f;

    protected override void OnInit()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(
                Confirm
            );
        }
    }

    private void RefreshTutorialText()
    {
        if (tutorialText == null)
        {
            return;
        }

        string primarySkillName =
            "冲击波";

        string secondarySkillName =
            "穿透射线";

        SkillManager skillManager =
            GameEntry.Skill;

        if (skillManager != null)
        {
            primarySkillName =
                skillManager.GetSkillDisplayName(
                    PlayerSkillInput.PrimarySkillId
                );

            secondarySkillName =
                skillManager.GetSkillDisplayName(
                    PlayerSkillInput.SecondarySkillId
                );
        }

        tutorialText.text =
            "WASD：移动\n" +
            "鼠标：瞄准\n" +
            "鼠标左键：射击\n" +
            "Left Shift：翻滚\n" +
            $"{PlayerSkillInput.PrimarySkillKeyText}：" +
            $"{primarySkillName}\n" +
            $"{PlayerSkillInput.SecondarySkillKeyText}：" +
            $"{secondarySkillName}";
    }
    
    protected override void OnOpen(object args)
    {
        if (!pauseGame)
        {
            return;
        }

        previousTimeScale =
            Time.timeScale;

        Time.timeScale = 0f;
    }

    protected override void OnClose()
    {
        if (!pauseGame)
        {
            return;
        }

        Time.timeScale =
            previousTimeScale;
    }

    private void Confirm()
    {
        PlayerPrefs.SetInt(
            TutorialPlayerPrefsKey,
            1
        );

        PlayerPrefs.Save();

        CloseSelf();
    }

    [ContextMenu("Reset Tutorial Flag")]
    private void ResetTutorialFlag()
    {
        PlayerPrefs.DeleteKey(
            TutorialPlayerPrefsKey
        );

        PlayerPrefs.Save();

        Debug.Log(
            "新手提示记录已经重置。",
            this
        );
    }
}
