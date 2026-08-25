using UnityEngine;
using System.Collections;

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
            uiManager.IsOpen(UIType.Result) ||
            uiManager.IsOpen(UIType.Tutorial))
        {
            return;
        }

        if (uiManager.IsOpen(UIType.Pause))
        {
            uiManager.Close(UIType.Pause);
            
            // 编辑器会在当前帧结束时处理 Esc 并释放鼠标，
            // 因此延迟到下一帧重新设置游戏鼠标状态。
            StartCoroutine(RestoreGameplayCursorNextFrame());
        }
        else
        {
            uiManager.Open(UIType.Pause);
        }
    }
    
    private IEnumerator RestoreGameplayCursorNextFrame()
    {
        // 等编辑器完成本帧的 Esc 处理。
        yield return null;

        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = false;
    }
    
}
