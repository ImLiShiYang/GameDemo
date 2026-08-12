using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Android 虚拟摇杆 UI 组件。
/// 当前只负责产生 -1 ~ 1 的二维输入值。
/// 后续让 PlayerInputRouter 读取 Value 即可。
/// </summary>
public class VirtualJoystick :
    MonoBehaviour,
    IPointerDownHandler,
    IDragHandler,
    IPointerUpHandler
{
    [SerializeField]
    private RectTransform background;

    [SerializeField]
    private RectTransform handle;

    [Range(0.1f, 1f)]
    [SerializeField]
    private float handleRange = 0.75f;

    public Vector2 Value { get; private set; }

    private void Awake()
    {
        if (background == null)
        {
            background =
                transform as RectTransform;
        }
    }

    public void OnPointerDown(
        PointerEventData eventData)
    {
        OnDrag(eventData);
    }

    public void OnDrag(
        PointerEventData eventData)
    {
        if (background == null)
        {
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                background,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return;
        }

        Rect rect =
            background.rect;

        float halfWidth =
            Mathf.Max(1f, rect.width * 0.5f);

        float halfHeight =
            Mathf.Max(1f, rect.height * 0.5f);

        Vector2 normalized =
            new Vector2(
                localPoint.x / halfWidth,
                localPoint.y / halfHeight
            );

        Value =
            Vector2.ClampMagnitude(
                normalized,
                1f
            );

        if (handle != null)
        {
            float radius =
                Mathf.Min(
                    halfWidth,
                    halfHeight
                ) * handleRange;

            handle.anchoredPosition =
                Value * radius;
        }
    }

    public void OnPointerUp(
        PointerEventData eventData)
    {
        Value = Vector2.zero;

        if (handle != null)
        {
            handle.anchoredPosition =
                Vector2.zero;
        }
    }
}
