using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TutorialOpenArgs
{
    public bool MarkAsShown = true;
    public Action OnConfirmed;
}

public class TutorialPanel : UIBase
{
   
    [SerializeField]
    private TMP_Text tutorialText;

    [SerializeField]
    private Button confirmButton;
    
    [SerializeField]
    private TMP_Text confirmButtonText;

    [SerializeField]
    private bool pauseGame = true;
    
    private TutorialOpenArgs currentArgs;

    private float previousTimeScale = 1f;
    private CursorLockMode previousCursorLockMode;
    private bool previousCursorVisible;

    protected override void OnInit()
    {
        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(
                Confirm
            );
        }
    }
    
    public static bool HasBeenShown
    {
        get
        {
            if (GameEntry.Save == null)
            {
                return false;
            }

            return GameEntry.Save.Data.player.tutorialShown;
        }
    }
    
    private void RefreshConfirmButtonText()
    {
        if (confirmButtonText == null)
        {
            return;
        }

        bool openedFromPause = currentArgs != null && !currentArgs.MarkAsShown;
        confirmButtonText.text = openedFromPause ? "返回" : "开始游戏";
    }

    private void RefreshTutorialText()
    {
        if (tutorialText == null)
        {
            return;
        }

        string primarySkillName = "冲击波";
        string primarySkillDescription =
            "对周围敌人造成范围伤害，并产生较强打断。";

        string secondarySkillName = "穿透射线";
        string secondarySkillDescription =
            "向瞄准方向发射射线，贯穿路径上的多个敌人。";

        SkillManager skillManager = GameEntry.Skill;

        if (skillManager != null)
        {
            primarySkillName =
                skillManager.GetSkillDisplayName(
                    PlayerSkillInput.PrimarySkillId
                );

            primarySkillDescription =
                skillManager.GetSkillDescription(
                    PlayerSkillInput.PrimarySkillId
                );

            secondarySkillName =
                skillManager.GetSkillDisplayName(
                    PlayerSkillInput.SecondarySkillId
                );

            secondarySkillDescription =
                skillManager.GetSkillDescription(
                    PlayerSkillInput.SecondarySkillId
                );
        }

        tutorialText.text =
            "【基础操作】\n" +
            "WASD：移动\n" +
            "鼠标：瞄准\n" +
            "鼠标左键：射击\n" +
            "空格键：翻滚\n" +
            "QE：旋转视角\n\n" +

            "【技能说明】\n" +
            $"{PlayerSkillInput.PrimarySkillKeyText}：{primarySkillName}\n" +
            $"{primarySkillDescription}\n\n" +

            $"{PlayerSkillInput.SecondarySkillKeyText}：{secondarySkillName}\n" +
            $"{secondarySkillDescription}";
    }
    
    protected override void OnOpen(object args)
    {
        currentArgs = args as TutorialOpenArgs;

        RefreshTutorialText();
        RefreshConfirmButtonText();
        
        if (!pauseGame)
        {
            return;
        }

        previousTimeScale = Time.timeScale;
        previousCursorLockMode = Cursor.lockState;
        previousCursorVisible = Cursor.visible;

        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
    
    protected override void OnRefresh(object args)
    {
        if (args is TutorialOpenArgs openArgs)
        {
            currentArgs = openArgs;
        }

        RefreshTutorialText();
        RefreshConfirmButtonText();
    }

    protected override void OnClose()
    {
        if (pauseGame)
        {
            Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursorLockMode;
            Cursor.visible = previousCursorVisible;
        }

        currentArgs = null;
    }

    private void Confirm()
    {
        TutorialOpenArgs confirmedArgs = currentArgs;

        if (confirmedArgs == null || confirmedArgs.MarkAsShown)
        {
            if (GameEntry.Save != null)
            {
                GameEntry.Save.Data.player.tutorialShown = true;

                GameEntry.Save.Save();
            }
        }

        GrayboxPlayerController player =FindObjectOfType<GrayboxPlayerController>();
            
        if (player != null)
        {
            player.WaitForAimMouseMovement();
        }

        
        CloseSelf();

        confirmedArgs?.OnConfirmed?.Invoke();
    }

    [ContextMenu("Reset Tutorial Flag")]
    private void ResetTutorialFlag()
    {
        if (GameEntry.Save != null)
        {
            GameEntry.Save.Data.player.tutorialShown = false;

            GameEntry.Save.Save();

            Debug.Log(
                "新手提示记录已经重置。",
                this
            );
        }
    }
}
