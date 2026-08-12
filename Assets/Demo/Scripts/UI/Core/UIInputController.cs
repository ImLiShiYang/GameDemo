using UnityEngine;

/// <summary>
/// UI 快捷键入口。
/// 当前先处理 Esc 打开 / 关闭暂停菜单。
/// </summary>
public class UIInputController : MonoBehaviour
{
    private void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape))
        {
            return;
        }

        if (GameEntry.Instance == null)
        {
            return;
        }

        UIManager uiManager = GameEntry.UI;

        if (uiManager == null ||
            !uiManager.HasConfig(UIType.Pause))
        {
            return;
        }

        // 升级选择和结算时不允许再打开 Pause。
        if (uiManager.IsOpen(UIType.MainMenu) ||
            uiManager.IsOpen(UIType.LevelUp) ||
            uiManager.IsOpen(UIType.Result))
        {
            return;
        }

        if (uiManager.IsOpen(UIType.Pause))
        {
            uiManager.Close(UIType.Pause);
        }
        else
        {
            uiManager.Open(UIType.Pause);
        }
    }
}
