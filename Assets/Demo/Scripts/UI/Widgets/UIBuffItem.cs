using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UIBuffItem : MonoBehaviour
{
    [SerializeField]
    private Image icon;

    [Tooltip("可选。Image Type 设置为 Filled。")]
    [SerializeField]
    private Image durationFill;

    [Tooltip("可选。用于显示 Buff 层数。")]
    [SerializeField]
    private TMP_Text stackText;

    public void SetData(Sprite sprite,int stack,float normalizedRemaining)
    {
        if (icon != null)
        {
            icon.sprite = sprite;
            icon.enabled = sprite != null;
        }

        if (durationFill != null)
        {
            durationFill.fillAmount =
                Mathf.Clamp01(normalizedRemaining);
        }

        if (stackText != null)
        {
            stackText.text =
                stack > 1
                    ? stack.ToString()
                    : string.Empty;
        }
    }
}
