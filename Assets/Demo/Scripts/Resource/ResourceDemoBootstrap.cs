using System;
using UnityEngine;

public class ResourceDemoBootstrap : MonoBehaviour
{
    private const string PanelAddress = "UI/AddressableDemoPanel";

    [SerializeField] private Transform uiRoot;

    private async void Start()
    {
        if (AddressableResourceManager.Instance == null)
        {
            Debug.LogError("场景中不存在 AddressableResourceManager。");
            return;
        }

        try
        {
            await AddressableResourceManager.Instance.InstantiateAsync(
                PanelAddress,
                uiRoot
            );
        }   
        catch (Exception exception)
        {
            Debug.LogError(
                $"演示 UI 加载失败：\n{exception}"
            );
        }
    }
}