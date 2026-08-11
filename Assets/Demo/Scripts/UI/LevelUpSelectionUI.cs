using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class UpgradeChoiceSlot
{
    public Button button;

    public Image icon;

    public TMP_Text nameText;

    public TMP_Text descriptionText;

    public TMP_Text levelText;
}

public class LevelUpSelectionUI : MonoBehaviour
{
    private static TMP_FontAsset runtimeChineseFont;

    [SerializeField]
    private GameObject root;

    [SerializeField]
    private UpgradeChoiceSlot[] slots;

    [Header("Font")]
    [Tooltip("Optional. The bundled SimHei font is used when empty.")]
    [SerializeField]
    private TMP_FontAsset chineseFont;

    private Action<UpgradeData> onSelected;

    private void Awake()
    {
        ApplyCardLayout();
        ApplyReadableFont();
    }

    /// <summary>
    /// 显示升级三选一界面。
    ///
    /// choices：这次要显示的升级配置，例如：
    /// 快速射击 / 穿透弹药 / 扩散射击。
    ///
    /// upgradeSystem：用于查询每个技能当前已经升级到几级。
    ///
    /// selectCallback：玩家点击某个技能后，要通知外部执行的回调。
    /// 一般传进来的是 LevelUpController.HandleUpgradeSelected。
    /// </summary>
    public void Show(IReadOnlyList<UpgradeData> choices,PlayerUpgradeSystem upgradeSystem,Action<UpgradeData> selectCallback)
    {
        /*
         * 保存外部传进来的“选择完成后的回调”。
         *
         * 例如：
         * LevelUpController 调用 Show 时传入：
         *
         * HandleUpgradeSelected
         *
         * 那么后面玩家点击技能时，
         * onSelected?.Invoke(upgrade)
         * 实际上就是在调用：
         *
         * HandleUpgradeSelected(upgrade)
         */
        onSelected = selectCallback;

        /*
         * root 就是整个升级面板 LevelUpPanel。
         *
         * 如果 Inspector 没有给 Root 拖引用，
         * 后面无法打开升级界面，所以直接报错并结束。
         */
        if (root == null)
        {
            Debug.LogError("LevelUpSelectionUI has no Root assigned.",this);
            return;
        }

        /*
         * 打开整个升级界面。
         *
         * 游戏开始时 LevelUpPanel 默认是关闭的，
         * 玩家升级以后在这里显示出来。
         */
        root.SetActive(true);

        // Keep every card readable even when its child RectTransforms were
        // accidentally left at the same anchored position in the scene.
        ApplyCardLayout();

        /*
         * 给 UI 文本应用可读字体。
         *
         * 这是你当前 UI 自己的显示处理，
         * 和升级逻辑本身没有直接关系。
         */
        ApplyReadableFont();

        /*
         * 遍历所有技能卡槽。
         *
         * 例如 slots.Length = 3：
         *
         * slots[0] → Choice_01
         * slots[1] → Choice_02
         * slots[2] → Choice_03
         */
        for (int i = 0; i < slots.Length; i++)
        {
            /*
             * 取出当前第 i 个 UI 卡槽。
             */
            UpgradeChoiceSlot slot = slots[i];

            /*
             * 判断当前这个卡槽有没有对应的升级配置。
             *
             * 例如：
             *
             * slots.Length = 3
             * choices.Count = 2
             *
             * i = 0 → true
             * i = 1 → true
             * i = 2 → false
             *
             * 第三个卡槽就不显示。
             */
            bool hasChoice = i < choices.Count;

            /*
             * 有对应技能就显示这个 Button，
             * 没有就隐藏。
             */
            slot.button.gameObject.SetActive(hasChoice);

            /*
             * 如果这个卡槽没有对应技能，
             * 当前循环后面的 UI 填充逻辑就不用执行了，
             * 直接处理下一个 slot。
             */
            if (!hasChoice)
            {
                continue;
            }

            /*
             * 取出当前卡槽对应的 UpgradeData。
             *
             * 例如：
             *
             * choices[0] → Upgrade_RapidFire
             * choices[1] → Upgrade_PiercingAmmo
             * choices[2] → Upgrade_SpreadShot
             */
            UpgradeData upgrade =choices[i];

            /*
             * 查询这个技能当前已经拥有几级。
             *
             * 例如：
             *
             * 快速射击目前已经 Lv.1
             *
             * currentLevel = 1
             */
            int currentLevel =upgradeSystem.GetUpgradeLevel(upgrade);

            /*
             * 把 UpgradeData 里的技能名称
             * 显示到当前卡片的 Name 文本上。
             *
             * 例如：
             *
             * Upgrade_RapidFire.DisplayName
             * =
             * "快速射击"
             */
            slot.nameText.text =upgrade.DisplayName;

            /*
             * 显示技能描述。
             *
             * 例如：
             * "射击间隔降低15%"
             */
            slot.descriptionText.text =upgrade.Description;

            /*
             * 显示“这一次选择后会变成几级”。
             *
             * currentLevel 是当前等级，
             * 所以显示 currentLevel + 1。
             *
             * 例如：
             *
             * 当前 Lv.1
             * 这次再选就是 Lv.2
             *
             * 所以 UI 显示：
             * Lv.2
             */
            slot.levelText.text =$"Lv.{currentLevel + 1}";

            /*
             * 如果这个卡槽配置了 Icon Image，
             * 就尝试显示技能图标。
             */
            if (slot.icon != null)
            {
                /*
                 * 把 UpgradeData 里的 Sprite
                 * 赋给当前 UI Image。
                 */
                slot.icon.sprite =upgrade.Icon;
                    

                /*
                 * 如果 UpgradeData 没有设置 Icon，
                 * 就直接把 Image 组件隐藏，
                 * 避免显示一个空白图片框。
                 */
                slot.icon.enabled =upgrade.Icon != null;
            }

            /*
             * 清掉这个 Button 之前绑定的所有点击事件。
             *
             * 因为这个升级界面会反复打开，
             * 如果不 RemoveAllListeners，
             *
             * 第一次打开绑定一次，
             * 第二次打开又绑定一次，
             * 第三次又绑定一次……
             *
             * 最后点一次按钮可能触发多次升级。
             */
            slot.button.onClick.RemoveAllListeners();

            /*
             * 确保当前 Button 可以点击。
             *
             * 因为玩家选择技能以后，
             * SelectUpgrade() 里可能会把按钮暂时禁用，
             * 所以下一次打开 UI 时要重新开启。
             */
            slot.button.interactable = true;

            /*
             * 保存当前循环对应的 UpgradeData。
             *
             * 例如当前 i = 0：
             *
             * upgrade = Upgrade_RapidFire
             *
             * capturedUpgrade 也就是 Upgrade_RapidFire。
             *
             * 这样当前 Button 就能记住：
             * “我代表的是快速射击。”
             */
            UpgradeData capturedUpgrade =upgrade;

            /*
             * 给当前 Button 动态注册点击事件。
             *
             * 玩家点击这个 Button 时执行：
             *
             * SelectUpgrade(capturedUpgrade)
             *
             * 例如当前卡片是快速射击，
             * 实际就是：
             *
             * SelectUpgrade(Upgrade_RapidFire)
             */
            slot.button.onClick.AddListener(() => SelectUpgrade(capturedUpgrade));
        }
    }

    /// <summary>
    /// 玩家点击某张升级卡后执行。
    /// </summary>
    private void SelectUpgrade(UpgradeData upgrade)
    {
        /*
         * 玩家已经完成一次选择，
         * 禁用所有技能按钮，防止快速连点导致
         * 一次升级同时选择多个技能。
         */
        foreach (UpgradeChoiceSlot slot in slots)
        {
            if (slot.button != null)
            {
                slot.button.interactable = false;
            }
        }

        /*
         * 通知外部：
         * “玩家选择了这个 UpgradeData。”
         *
         * onSelected 在 Show() 时保存的是
         * LevelUpController.HandleUpgradeSelected。
         *
         * 因此这里实际上相当于：
         *
         * HandleUpgradeSelected(upgrade);
         */
        onSelected?.Invoke(upgrade);
    }

    private void ApplyReadableFont()
    {
        TMP_FontAsset font = chineseFont != null
            ? chineseFont
            : GetRuntimeChineseFont();

        if (font == null)
        {
            return;
        }

        GameObject textRoot = root != null
            ? root
            : gameObject;

        TMP_Text[] texts =
            textRoot.GetComponentsInChildren<TMP_Text>(true);

        foreach (TMP_Text text in texts)
        {
            text.font = font;
        }
    }

    private void ApplyCardLayout()
    {
        if (slots == null)
        {
            return;
        }

        foreach (UpgradeChoiceSlot slot in slots)
        {
            if (slot == null)
            {
                continue;
            }

            SetTextLayout(
                slot.nameText,
                new Vector2(0f, 55f),
                new Vector2(330f, 60f),
                30f,
                FontStyles.Bold,
                new Color32(45, 45, 45, 255));

            SetTextLayout(
                slot.descriptionText,
                new Vector2(0f, -45f),
                new Vector2(330f, 130f),
                22f,
                FontStyles.Normal,
                new Color32(65, 65, 65, 255));

            SetTextLayout(
                slot.levelText,
                new Vector2(0f, -190f),
                new Vector2(200f, 50f),
                20f,
                FontStyles.Normal,
                new Color32(90, 90, 90, 255));

            if (slot.icon != null)
            {
                RectTransform iconRect = slot.icon.rectTransform;
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.anchoredPosition = new Vector2(0f, 155f);
                iconRect.sizeDelta = new Vector2(120f, 120f);
                slot.icon.preserveAspect = true;
            }
        }
    }

    private static void SetTextLayout(
        TMP_Text text,
        Vector2 anchoredPosition,
        Vector2 size,
        float fontSize,
        FontStyles fontStyle,
        Color color)
    {
        if (text == null)
        {
            return;
        }

        RectTransform rect = text.rectTransform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = fontSize;
        text.fontStyle = fontStyle;
        text.color = color;
        text.enableWordWrapping = true;
    }
    private static TMP_FontAsset GetRuntimeChineseFont()
    {
        if (runtimeChineseFont != null)
        {
            return runtimeChineseFont;
        }

        Font systemFont =
            Resources.Load<Font>("Fonts/SimHei");

        if (systemFont == null)
        {
            Debug.LogWarning(
                "The bundled SimHei font could not be loaded. The upgrade UI will keep its default TMP font."
            );

            return null;
        }

        runtimeChineseFont =
            TMP_FontAsset.CreateFontAsset(systemFont);

        if (runtimeChineseFont == null)
        {
            Debug.LogWarning(
                "TMP could not create the Chinese font asset. The upgrade UI will keep its default font."
            );

            return null;
        }

        runtimeChineseFont.name =
            "Runtime Chinese UI Font";

        runtimeChineseFont.atlasPopulationMode =
            AtlasPopulationMode.Dynamic;

        return runtimeChineseFont;
    }

    public void Hide()
    {
        if (root != null)
        {
            root.SetActive(false);
        }
    }
}