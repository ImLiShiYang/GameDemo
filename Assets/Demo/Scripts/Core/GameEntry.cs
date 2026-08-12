using UnityEngine;

/// <summary>
/// 游戏全局模块入口。
/// 当前统一管理：
/// PoolManager
/// UIManager
/// </summary>
public class GameEntry : MonoBehaviour
{
    public static GameEntry Instance
    {
        get;
        private set;
    }

    [Header("Managers")]

    [SerializeField]
    private PoolManager poolManager;

    [SerializeField]
    private UIManager uiManager;

    /// <summary>
    /// 全局对象池入口。
    /// </summary>
    public static PoolManager Pool
    {
        get
        {
            if (Instance == null)
            {
                Debug.LogError(
                    "场景中没有 GameEntry。"
                );

                return null;
            }

            if (Instance.poolManager == null)
            {
                Debug.LogError(
                    "GameEntry 没有找到 PoolManager。",
                    Instance
                );

                return null;
            }

            return Instance.poolManager;
        }
    }

    /// <summary>
    /// 全局 UI 管理器入口。
    /// </summary>
    public static UIManager UI
    {
        get
        {
            if (Instance == null)
            {
                Debug.LogError(
                    "场景中没有 GameEntry。"
                );

                return null;
            }

            if (Instance.uiManager == null)
            {
                Debug.LogError(
                    "GameEntry 没有找到 UIManager。",
                    Instance
                );

                return null;
            }

            return Instance.uiManager;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError(
                "场景中存在多个 GameEntry，只允许保留一个。",
                this
            );

            Destroy(gameObject);

            return;
        }

        Instance = this;

        // 自动寻找 PoolManager。
        if (poolManager == null)
        {
            poolManager =
                GetComponentInChildren<PoolManager>(
                    true
                );
        }

        // 自动寻找 UIManager。
        if (uiManager == null)
        {
            uiManager =
                GetComponentInChildren<UIManager>(
                    true
                );
        }

        if (poolManager == null)
        {
            Debug.LogError(
                "GameEntry 下没有找到 PoolManager。",
                this
            );
        }

        if (uiManager == null)
        {
            Debug.LogError(
                "GameEntry 下没有找到 UIManager。",
                this
            );
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}