using System;
using System.Collections.Generic;
using UnityEngine;

public class BuffManager : MonoBehaviour
{
    private class ActiveBuff
    {
        public string Id;
        public float Duration;
        public float Remaining;
        public int Stack;
        public int MaxStack;
        public bool RefreshOnAdd;
        public string EffectType;
        public float Value;
        public UIBuffData UIData;
    }

    private const string BuffConfigModule = "Buff.BuffConfig";

    [SerializeField]
    private Health playerHealth;
    
    [SerializeField]
    private GrayboxPlayerController playerController;

    [Header("Debug")]
    [SerializeField]
    private bool enableDebugInput = true;

    //测试按键
    [SerializeField]
    private KeyCode debugAddBuffKey = KeyCode.B;
    [SerializeField]
    private KeyCode debugAddSpeedBuffKey = KeyCode.N;

    private readonly Dictionary<string, ActiveBuff> activeBuffs =new Dictionary<string, ActiveBuff>();

    private readonly List<string> expiredBuffIds =new List<string>();

    private readonly List<string> depletedShieldBuffIds = new List<string>();

    private Health subscribedPlayerHealth;

    private void Awake()
    {
        FindPlayerReferences();
    }

    private void OnDestroy()
    {
        BindPlayerHealthEvents(null);
    }

    private void Update()
    {
        //测试按键 直接添加buff
        if (enableDebugInput && Input.GetKeyDown(debugAddBuffKey))
        {
            AddBuff("EnergyShield");
        }
        if (enableDebugInput && Input.GetKeyDown(debugAddSpeedBuffKey))
        {
            AddBuff("SpeedBoost");
        }

        UpdateBuffs();
    }

    public void AddBuff(string buffId)
    {
        // 1. 先检查传入的 BuffId 是否有效。
        // 如果是 null、空字符串或者只有空格，就直接结束。
        if (string.IsNullOrWhiteSpace(buffId))
        {
            return;
        }

        // 2. 从 GameEntry 获取全局 LuaManager。
        // Buff 的配置数据目前存放在 Lua 的 BuffConfig 中。
        LuaManager luaManager = GameEntry.Lua;

        // LuaManager 不存在时无法读取 Buff 配置。
        if (luaManager == null)
        {
            return;
        }

        // 3. 调用 Lua 模块 Buff.BuffConfig 中的 GetBuffValues(buffId)。
        //
        // Lua 会根据 buffId 返回：
        // results[0] = duration
        // results[1] = maxStack
        // results[2] = refreshOnAdd
        // results[3] = effectType
        // results[4] = value
        // results[5] = iconPath
        object[] results = luaManager.CallWithResults(
            BuffConfigModule,
            "GetBuffValues",
            buffId
        );

        // 4. Lua 配置读取失败，或者返回的数据数量不足 6 个，
        // 说明这个 Buff 的配置不完整。
        if (results == null || results.Length < 6)
        {
            Debug.LogError(
                $"读取 Buff 配置失败：{buffId}",
                this
            );

            return;
        }

        // 5. 把 Lua 返回的 object 数据转换成 C# 中真正需要的类型。
        float duration = Convert.ToSingle(results[0]);
        int maxStack = Mathf.Max(1, Convert.ToInt32(results[1]));
        bool refreshOnAdd = Convert.ToBoolean(results[2]);
        string effectType = Convert.ToString(results[3]);
        float value = Convert.ToSingle(results[4]);
        string iconPath = Convert.ToString(results[5]);

        // 6. 先检查这个 Buff 是否已经存在。
        //
        // activeBuffs：
        // key   = buffId
        // value = 当前正在生效的 ActiveBuff
        if (activeBuffs.TryGetValue(buffId, out ActiveBuff activeBuff))
        {
            // 7. 如果 Buff 已经存在，并且当前层数还没有达到最大层数，
            // 就增加一层。
            if (activeBuff.Stack < activeBuff.MaxStack)
            {
                activeBuff.Stack++;

                // 增加新的一层以后，立即应用这一层带来的效果。
                //
                // 例如：
                // Shield    → 增加一层护盾
                // MoveSpeed → 重新计算移动速度倍率
                ApplyAddedStack(activeBuff);
            }

            // 8. 如果配置了“重复获得 Buff 时刷新持续时间”，
            // 就把剩余时间重新恢复到完整持续时间。
            //
            // 例如：
            // Duration = 6 秒
            // 当前只剩 2 秒
            // 再次获得 Buff
            // Remaining 重新变成 6 秒
            if (activeBuff.RefreshOnAdd)
            {
                activeBuff.Remaining = activeBuff.Duration;
            }

            // 9. Buff 的层数或持续时间发生变化后，
            // 刷新 HUD 上对应的 Buff 图标、层数和剩余时间。
            RefreshHUD(activeBuff);

            Debug.Log(
                $"刷新 Buff：{buffId}，层数：{activeBuff.Stack}，剩余：{activeBuff.Remaining:F1}s",
                this
            );

            // 已经处理完“重复获得 Buff”的情况，
            // 不再继续创建新的 ActiveBuff。
            return;
        }

        // 10. 如果这个 Buff 当前不存在，
        // 就根据 Lua 配置创建一个新的 ActiveBuff。
        ActiveBuff newBuff = new ActiveBuff
        {
            // Buff 唯一 ID。
            Id = buffId,

            // Buff 总持续时间。
            Duration = duration,

            // 第一次获得时，剩余时间等于总持续时间。
            Remaining = duration,

            // 第一次获得默认从 1 层开始。
            Stack = 1,

            // 最大允许叠加层数。
            MaxStack = maxStack,

            // 重复获得时是否刷新持续时间。
            RefreshOnAdd = refreshOnAdd,

            // Buff 效果类型。
            //
            // 例如：
            // "Shield"
            // "MoveSpeed"
            EffectType = effectType,

            // 每层效果数值。
            //
            // 例如：
            // Shield    value = 25
            // MoveSpeed value = 0.2
            Value = value,

            // 11. 创建这个 Buff 对应的 HUD 数据。
            UIData = new UIBuffData
            {
                // HUD 用 BuffId 区分不同 Buff。
                Id = buffId,

                // 如果 Lua 配置了 iconPath，
                // 就通过 Resources.Load 加载对应的 Sprite。
                Icon = string.IsNullOrWhiteSpace(iconPath)
                    ? null
                    : Resources.Load<Sprite>(iconPath),

                // 第一次获得时显示 1 层。
                Stack = 1,

                // 刚获得时剩余时间是 100%。
                NormalizedRemaining = 1f
            }
        };

        // 12. 把新 Buff 放入 activeBuffs。
        //
        // 从这一刻开始，UpdateBuffs() 就会每帧更新它的剩余时间。
        activeBuffs.Add(buffId, newBuff);

        // 13. 应用第一层 Buff 的实际效果。
        //
        // 例如：
        // EnergyShield
        // → Health.AddShield(...)
        //
        // SpeedBoost
        // → RefreshMoveSpeedEffect()
        ApplyAddedStack(newBuff);

        // 14. 把新 Buff 显示到 HUD。
        RefreshHUD(newBuff);

        Debug.Log(
            $"获得 Buff：{buffId}，持续 {duration} 秒。",
            this
        );
    }

    public void RemoveBuff(string buffId)
    {
        if (!activeBuffs.TryGetValue(buffId, out ActiveBuff buff))
        {
            return;
        }

        activeBuffs.Remove(buffId);

        RemoveEffect(buff);

        HUDPanel hudPanel = GetHUDPanel();

        if (hudPanel != null)
        {
            hudPanel.RemoveBuff(buffId);
        }

        Debug.Log(
            $"Buff 到期移除：{buffId}",
            this
        );
    }

    public bool HasBuff(string buffId)
    {
        return activeBuffs.ContainsKey(buffId);
    }

    public int GetBuffStack(string buffId)
    {
        if (!activeBuffs.TryGetValue(buffId, out ActiveBuff buff))
        {
            return 0;
        }

        return buff.Stack;
    }

    private void UpdateBuffs()
    {
        if (activeBuffs.Count == 0)
        {
            return;
        }

        expiredBuffIds.Clear();

        foreach (KeyValuePair<string, ActiveBuff> pair in activeBuffs)
        {
            ActiveBuff buff = pair.Value;

            buff.Remaining = Mathf.Max(
                0f,
                buff.Remaining - Time.deltaTime
            );

            RefreshHUD(buff);

            if (buff.Remaining <= 0f)
            {
                expiredBuffIds.Add(buff.Id);
            }
        }

        foreach (string buffId in expiredBuffIds)
        {
            RemoveBuff(buffId);
        }
    }

    private void ApplyAddedStack(ActiveBuff buff)
    {
        FindPlayerReferences();

        switch (buff.EffectType)
        {
            case "Shield":
                playerHealth.AddShield(buff.Value);
                break;

            case "MoveSpeed":
                RefreshMoveSpeedEffect();
                break;
            
            default:
                Debug.LogWarning(
                    $"没有对应的 Buff 效果：{buff.EffectType}",
                    this
                );
                break;
        }
    }

    private void RefreshMoveSpeedEffect()
    {
        FindPlayerReferences();

        if (playerController == null)
        {
            return;
        }

        float bonus = 0f;

        foreach (KeyValuePair<string, ActiveBuff> pair in activeBuffs)
        {
            ActiveBuff buff = pair.Value;

            if (buff.EffectType != "MoveSpeed")
            {
                continue;
            }

            bonus += buff.Value * buff.Stack;
        }

        playerController.SetMoveSpeedMultiplier(1f + bonus);
    }
    
    private void RemoveEffect(ActiveBuff buff)
    {
        FindPlayerReferences();

        switch (buff.EffectType)
        {
            case "Shield":
                if (playerHealth != null)
                {
                    playerHealth.ClearShield();
                }
                break;

            case "MoveSpeed":
                RefreshMoveSpeedEffect();
                break;
        }
    }

    private void RefreshHUD(ActiveBuff buff)
    {
        HUDPanel hudPanel = GetHUDPanel();

        if (hudPanel == null)
        {
            return;
        }

        buff.UIData.Stack = buff.Stack;

        buff.UIData.NormalizedRemaining =
            buff.Duration > 0f
                ? buff.Remaining / buff.Duration
                : 1f;

        hudPanel.SetBuff(buff.UIData);
    }

    private HUDPanel GetHUDPanel()
    {
        UIManager uiManager = GameEntry.UI;

        if (uiManager == null)
        {
            return null;
        }

        return uiManager.Get<HUDPanel>(UIType.HUD);
    }

    private void FindPlayerReferences()
    {
        if (playerController == null)
        {
            playerController = FindObjectOfType<GrayboxPlayerController>();
        }

        if (playerHealth == null && playerController != null)
        {
            playerHealth = playerController.GetComponent<Health>();
        }

        BindPlayerHealthEvents(playerHealth);
    }

    private void BindPlayerHealthEvents(Health health)
    {
        // 如果当前已经订阅的 Health
        // 和这次传进来的 Health 是同一个对象，
        // 说明已经绑定过了，不需要重复绑定。
        if (subscribedPlayerHealth == health)
        {
            return;
        }

        // 如果之前已经绑定过某个玩家的 Health，
        // 先取消旧的 ShieldDepleted 事件监听。
        //
        // 这样可以避免：
        // 1. 重复订阅
        // 2. 玩家对象切换后，旧对象仍然触发事件
        // 3. HandleShieldDepleted 被调用多次
        if (subscribedPlayerHealth != null)
        {
            subscribedPlayerHealth.ShieldDepleted -=
                HandleShieldDepleted;
        }

        // 保存新的 Health 引用。
        //
        // 从这一行开始，
        // subscribedPlayerHealth 就代表当前正在监听的玩家 Health。
        subscribedPlayerHealth = health;

        // 如果新的 Health 有效，
        // 就监听它的 ShieldDepleted 事件。
        //
        // 当玩家护盾被完全打空时：
        //
        // Health
        //   ↓
        // ShieldDepleted
        //   ↓
        // HandleShieldDepleted()
        //
        // 然后 BuffManager 会去删除对应的 Shield Buff。
        if (subscribedPlayerHealth != null)
        {
            subscribedPlayerHealth.ShieldDepleted +=
                HandleShieldDepleted;
        }
    }

    private void HandleShieldDepleted()
    {
        depletedShieldBuffIds.Clear();

        foreach (KeyValuePair<string, ActiveBuff> pair in activeBuffs)
        {
            if (pair.Value.EffectType == "Shield")
            {
                depletedShieldBuffIds.Add(pair.Key);
            }
        }

        foreach (string buffId in depletedShieldBuffIds)
        {
            RemoveBuff(buffId);
        }
    }
}
