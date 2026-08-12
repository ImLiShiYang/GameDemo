using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class UIUpgradeChoiceSlot
{
    public Button button;
    public Image icon;
    public TMP_Text nameText;
    public TMP_Text descriptionText;
    public TMP_Text levelText;
}

public class UpgradePanelArgs
{
    public IReadOnlyList<UpgradeData> Choices;
    public PlayerUpgradeSystem UpgradeSystem;
    public Action<UpgradeData> OnSelected;
}

/// <summary>
/// 统一接入 UIManager 的升级三选一界面。
/// 替代旧 LevelUpSelectionUI 的 Show / Hide 管理方式。
/// </summary>
public class UpgradePanel : UIBase
{
    [SerializeField]
    private UIUpgradeChoiceSlot[] slots;

    private IReadOnlyList<UpgradeData> choices;
    private PlayerUpgradeSystem upgradeSystem;
    private Action<UpgradeData> onSelected;

    private bool selectionLocked;

    protected override void OnInit()
    {
        if (slots == null)
        {
            return;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            int capturedIndex = i;

            UIUpgradeChoiceSlot slot =
                slots[i];

            if (slot == null ||
                slot.button == null)
            {
                continue;
            }

            slot.button.onClick.AddListener(
                () => Select(capturedIndex)
            );
        }
    }

    protected override void OnOpen(object args)
    {
        ApplyArgs(args);
    }

    protected override void OnRefresh(object args)
    {
        ApplyArgs(args);
    }

    protected override void OnClose()
    {
        choices = null;
        upgradeSystem = null;
        onSelected = null;
        selectionLocked = false;
    }

    private void ApplyArgs(object args)
    {
        UpgradePanelArgs panelArgs =
            args as UpgradePanelArgs;

        if (panelArgs == null ||
            panelArgs.Choices == null ||
            panelArgs.UpgradeSystem == null)
        {
            Debug.LogError(
                "UpgradePanel 打开参数不完整。",
                this
            );

            return;
        }

        choices =
            panelArgs.Choices;

        upgradeSystem =
            panelArgs.UpgradeSystem;

        onSelected =
            panelArgs.OnSelected;

        selectionLocked = false;

        RefreshSlots();
    }

    private void RefreshSlots()
    {
        if (slots == null)
        {
            return;
        }

        for (int i = 0; i < slots.Length; i++)
        {
            UIUpgradeChoiceSlot slot =
                slots[i];

            if (slot == null ||
                slot.button == null)
            {
                continue;
            }

            bool hasChoice =
                choices != null &&
                i < choices.Count;

            slot.button.gameObject.SetActive(
                hasChoice
            );

            if (!hasChoice)
            {
                continue;
            }

            UpgradeData upgrade =
                choices[i];

            int currentLevel =
                upgradeSystem.GetUpgradeLevel(
                    upgrade
                );

            slot.button.interactable = true;

            if (slot.nameText != null)
            {
                slot.nameText.text =
                    upgrade.DisplayName;
            }

            if (slot.descriptionText != null)
            {
                slot.descriptionText.text =
                    upgrade.Description;
            }

            if (slot.levelText != null)
            {
                slot.levelText.text =
                    $"Lv.{currentLevel + 1}";
            }

            if (slot.icon != null)
            {
                slot.icon.sprite =
                    upgrade.Icon;

                slot.icon.enabled =
                    upgrade.Icon != null;
            }
        }
    }

    private void Select(int index)
    {
        if (selectionLocked ||
            choices == null ||
            index < 0 ||
            index >= choices.Count)
        {
            return;
        }

        selectionLocked = true;

        SetAllButtonsInteractable(false);

        UpgradeData selected =
            choices[index];

        onSelected?.Invoke(selected);
    }

    private void SetAllButtonsInteractable(
        bool interactable)
    {
        if (slots == null)
        {
            return;
        }

        foreach (UIUpgradeChoiceSlot slot in slots)
        {
            if (slot != null &&
                slot.button != null)
            {
                slot.button.interactable =
                    interactable;
            }
        }
    }
}
