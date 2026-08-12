using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class UIConfig
{
    [Tooltip("界面类型。")]
    public UIType type;

    [Tooltip("界面 Prefab，Prefab 根节点必须挂 UIBase 子类。")]
    public UIBase prefab;

    [Tooltip("界面实例化到哪个层。")]
    public UILayer layer = UILayer.Normal;

    [Tooltip("关闭后是否缓存实例。HUD / Pause / LevelUp 建议开启。")]
    public bool cache = true;
}

/// <summary>
/// UGUI 界面总管理器。
///
/// 负责：
/// 1. 界面加载
/// 2. 界面打开 / 关闭
/// 3. UI 层级
/// 4. 界面缓存
/// 5. 防止重复实例化
/// </summary>
public class UIManager : MonoBehaviour
{
    [Header("UI Layers")]
    [SerializeField]
    private Transform hudRoot;

    [SerializeField]
    private Transform normalRoot;

    [SerializeField]
    private Transform popupRoot;

    [SerializeField]
    private Transform topRoot;

    [Header("UI Prefab Config")]
    [SerializeField]
    private List<UIConfig> configs = new List<UIConfig>();

    private readonly Dictionary<UIType, UIConfig> configMap =
        new Dictionary<UIType, UIConfig>();

    private readonly Dictionary<UIType, UIBase> instances =
        new Dictionary<UIType, UIBase>();

    private void Awake()
    {
        BuildConfigMap();
    }

    private void BuildConfigMap()
    {
        configMap.Clear();

        foreach (UIConfig config in configs)
        {
            if (config == null || config.prefab == null)
            {
                continue;
            }

            if (configMap.ContainsKey(config.type))
            {
                Debug.LogWarning(
                    $"UIManager 中重复配置了 {config.type}，后面的配置将覆盖前面的配置。",
                    this
                );
            }

            configMap[config.type] = config;
        }
    }

    public bool HasConfig(UIType type)
    {
        return configMap.ContainsKey(type);
    }

    public UIBase Open(UIType type, object args = null)
    {
        if (!configMap.TryGetValue(type, out UIConfig config))  
        {
            Debug.LogError($"UIManager 没有配置界面：{type}。",this);
            return null;
        }

        if (!instances.TryGetValue(type, out UIBase ui) || ui == null)
        {
            Transform parent = GetLayerRoot(config.layer);

            ui = Instantiate(
                config.prefab,
                parent,
                false
            );

            ui.name = config.prefab.name;
            ui.AssignType(type);

            instances[type] = ui;
        }

        ui.OpenInternal(args);

        return ui;
    }

    public T Open<T>(UIType type, object args = null)
        where T : UIBase
    {
        return Open(type, args) as T;
    }

    public void Close(UIType type)
    {
        if (!instances.TryGetValue(type, out UIBase ui) || ui == null)
        {
            return;
        }

        ui.CloseInternal();

        if (!configMap.TryGetValue(type, out UIConfig config))
        {
            instances.Remove(type);
            Destroy(ui.gameObject);
            return;
        }

        if (!config.cache)
        {
            instances.Remove(type);
            Destroy(ui.gameObject);
        }
    }

    public void CloseAll()
    {
        List<UIType> openedTypes =
            new List<UIType>(instances.Keys);

        foreach (UIType type in openedTypes)
        {
            Close(type);
        }
    }

    public void CloseLayer(UILayer layer)
    {
        List<UIType> typesToClose =
            new List<UIType>();

        foreach (KeyValuePair<UIType, UIBase> pair in instances)
        {
            if (!pair.Value.IsOpen)
            {
                continue;
            }

            if (!configMap.TryGetValue(pair.Key, out UIConfig config))
            {
                continue;
            }

            if (config.layer == layer)
            {
                typesToClose.Add(pair.Key);
            }
        }

        foreach (UIType type in typesToClose)
        {
            Close(type);
        }
    }

    public bool IsOpen(UIType type)
    {
        return
            instances.TryGetValue(type, out UIBase ui) &&
            ui != null &&
            ui.IsOpen;
    }

    public T Get<T>(UIType type)
        where T : UIBase
    {
        if (!instances.TryGetValue(type, out UIBase ui))
        {
            return null;
        }

        return ui as T;
    }

    private Transform GetLayerRoot(UILayer layer)
    {
        switch (layer)
        {
            case UILayer.HUD:
                return hudRoot != null ? hudRoot : transform;

            case UILayer.Normal:
                return normalRoot != null ? normalRoot : transform;

            case UILayer.Popup:
                return popupRoot != null ? popupRoot : transform;

            case UILayer.Top:
                return topRoot != null ? topRoot : transform;

            default:
                return transform;
        }
    }
}
