using UnityEngine;

/// <summary>
/// 游戏全局模块入口。
/// 当前先统一暴露对象池，后续 AudioManager、UIManager 等模块也可以继续挂在这里。
/// </summary>
public class GameEntry : MonoBehaviour
{
    public static GameEntry Instance { get; private set; }

    [SerializeField]
    private PoolManager poolManager;

    public static PoolManager Pool
    {
        get
        {
            if (Instance == null)
            {
                Debug.LogError("场景中没有 GameEntry，请先创建 GameEntry 根对象并挂载 GameEntry 组件。");
                return null;
            }

            if (Instance.poolManager == null)
            {
                Debug.LogError("GameEntry 没有找到 PoolManager，请在 GameEntry 子物体上挂载 PoolManager。", Instance);
                return null;
            }

            return Instance.poolManager;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("场景中存在多个 GameEntry，只允许保留一个。", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (poolManager == null)
        {
            poolManager = GetComponentInChildren<PoolManager>(true);
        }

        if (poolManager == null)
        {
            Debug.LogError(
                "GameEntry 下没有找到 PoolManager。请创建子物体 PoolManager 并挂载 PoolManager 组件。",
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
