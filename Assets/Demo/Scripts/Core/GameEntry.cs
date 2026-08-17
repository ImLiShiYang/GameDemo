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
    
    [SerializeField]
    private WaveManager waveManager;

    [SerializeField]
    private LuaManager luaManager;

    [SerializeField]
    private SkillManager skillManager;
    
    [SerializeField]
    private BuffManager buffManager;
    
    public static BuffManager Buff
    {
        get
        {
            if (Instance == null)
            {
                Debug.LogError("场景中没有 GameEntry。");
                return null;
            }

            if (Instance.buffManager == null)
            {
                Debug.LogError(
                    "GameEntry 没有找到 BuffManager。",
                    Instance
                );

                return null;
            }

            return Instance.buffManager;
        }
    }
    
    public static SkillManager Skill
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

            if (Instance.skillManager == null)
            {
                Debug.LogError(
                    "GameEntry 没有找到 SkillManager。",
                    Instance
                );

                return null;
            }

            return Instance.skillManager;
        }
    }
    
    /// <summary>
    /// 全局 Lua 管理器入口。
    /// </summary>
    public static LuaManager Lua
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

            if (Instance.luaManager == null)
            {
                Debug.LogError(
                    "GameEntry 没有找到 LuaManager。",
                    Instance
                );

                return null;
            }

            return Instance.luaManager;
        }
    }

    /// <summary>
    /// 全局波次管理器入口。
    /// </summary>
    public static WaveManager Wave
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

            if (Instance.waveManager == null)
            {
                Debug.LogError(
                    "GameEntry 没有找到 WaveManager。",
                    Instance
                );

                return null;
            }

            return Instance.waveManager;
        }
    }

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
        
        // 自动寻找 WaveManager。
        if (waveManager == null)
        {
            waveManager =
                GetComponentInChildren<WaveManager>(
                    true
                );
        }

        // LuaManager 统一由 GameEntry 持有。
        // 场景中没有配置时，自动挂到 GameEntry 自身。
        if (luaManager == null)
        {
            luaManager =
                GetComponentInChildren<LuaManager>(
                    true
                );
        }

        if (luaManager == null)
        {
            luaManager =
                gameObject.AddComponent<LuaManager>();
        }

        if (skillManager == null)
        {
            skillManager =GetComponentInChildren<SkillManager>(true);
        }

        if (skillManager == null)
        {
            skillManager =
                gameObject.AddComponent<SkillManager>();
        }
        
        if (buffManager == null)
        {
            buffManager = GetComponentInChildren<BuffManager>(true);
        }

        if (buffManager == null)
        {
            buffManager = gameObject.AddComponent<BuffManager>();
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
        
        if (waveManager == null)
        {
            Debug.LogError(
                "GameEntry 下没有找到 WaveManager。",
                this
            );
        }

        if (luaManager == null)
        {
            Debug.LogError(
                "GameEntry 没有成功创建 LuaManager。",
                this
            );
        }
        
        if (skillManager == null)
        {
            Debug.LogError(
                "GameEntry 没有成功创建 skillManager。",
                this
            );
        }
        
        if (buffManager == null)
        {
            Debug.LogError(
                "GameEntry 没有成功创建 buffManager。",
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