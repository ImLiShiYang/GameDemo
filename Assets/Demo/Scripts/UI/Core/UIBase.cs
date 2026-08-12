using UnityEngine;

/// <summary>
/// 所有界面的基类。
///
/// 生命周期：
/// 第一次打开：OnInit -> OnOpen
/// 关闭：OnClose
/// 再次打开：OnOpen
/// 已经打开时再次 Open：OnRefresh
/// </summary>
public abstract class UIBase : MonoBehaviour
{
    public UIType Type { get; private set; }

    public bool IsOpen { get; private set; }

    private bool initialized;

    internal void AssignType(UIType type)
    {
        Type = type;
    }

    internal void OpenInternal(object args)
    {
        if (!initialized)
        {
            initialized = true;
            OnInit();
        }

        if (IsOpen)
        {
            transform.SetAsLastSibling();
            OnRefresh(args);
            return;
        }

        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        IsOpen = true;

        OnOpen(args);
    }

    internal void CloseInternal()
    {
        if (!IsOpen)
        {
            return;
        }

        OnClose();

        IsOpen = false;

        gameObject.SetActive(false);
    }

    /// <summary>
    /// 只执行一次。
    /// 适合绑定 Button.onClick 等不会随开关变化的事件。
    /// </summary>
    protected virtual void OnInit()
    {
    }

    /// <summary>
    /// 每次从关闭状态变为打开状态时执行。
    /// 适合注册外部事件、刷新数据。
    /// </summary>
    protected virtual void OnOpen(object args)
    {
    }

    /// <summary>
    /// 界面已经打开，又再次调用 Open 时执行。
    /// </summary>
    protected virtual void OnRefresh(object args)
    {
    }

    /// <summary>
    /// 每次关闭时执行。
    /// 在 OnOpen 注册的外部事件，应在这里解除。
    /// </summary>
    protected virtual void OnClose()
    {
    }

    protected void CloseSelf()
    {
        if (GameEntry.Instance == null)
        {
            return;
        }

        UIManager uiManager = GameEntry.UI;

        if (uiManager != null)
        {
            uiManager.Close(Type);
        }
    }
}
