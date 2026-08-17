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

    [Header("Debug")]
    [SerializeField]
    private bool enableDebugInput = true;

    [SerializeField]
    private KeyCode debugAddBuffKey = KeyCode.B;

    private readonly Dictionary<string, ActiveBuff> activeBuffs =
        new Dictionary<string, ActiveBuff>();

    private readonly List<string> expiredBuffIds =
        new List<string>();

    private readonly List<string> depletedShieldBuffIds =
        new List<string>();

    private Health subscribedPlayerHealth;

    private void Awake()
    {
        FindPlayerHealth();
    }

    private void OnDestroy()
    {
        BindPlayerHealthEvents(null);
    }

    private void Update()
    {
        if (enableDebugInput && Input.GetKeyDown(debugAddBuffKey))
        {
            AddBuff("EnergyShield");
        }

        UpdateBuffs();
    }

    public void AddBuff(string buffId)
    {
        if (string.IsNullOrWhiteSpace(buffId))
        {
            return;
        }

        LuaManager luaManager = GameEntry.Lua;

        if (luaManager == null)
        {
            return;
        }

        object[] results = luaManager.CallWithResults(
            BuffConfigModule,
            "GetBuffValues",
            buffId
        );

        if (results == null || results.Length < 6)
        {
            Debug.LogError(
                $"读取 Buff 配置失败：{buffId}",
                this
            );

            return;
        }

        float duration = Convert.ToSingle(results[0]);
        int maxStack = Mathf.Max(1, Convert.ToInt32(results[1]));
        bool refreshOnAdd = Convert.ToBoolean(results[2]);
        string effectType = Convert.ToString(results[3]);
        float value = Convert.ToSingle(results[4]);
        string iconPath = Convert.ToString(results[5]);

        if (activeBuffs.TryGetValue(buffId, out ActiveBuff activeBuff))
        {
            if (activeBuff.Stack < activeBuff.MaxStack)
            {
                activeBuff.Stack++;
                ApplyAddedStack(activeBuff);
            }

            if (activeBuff.RefreshOnAdd)
            {
                activeBuff.Remaining = activeBuff.Duration;
            }

            RefreshHUD(activeBuff);

            Debug.Log(
                $"刷新 Buff：{buffId}，层数：{activeBuff.Stack}，剩余：{activeBuff.Remaining:F1}s",
                this
            );

            return;
        }

        ActiveBuff newBuff = new ActiveBuff
        {
            Id = buffId,
            Duration = duration,
            Remaining = duration,
            Stack = 1,
            MaxStack = maxStack,
            RefreshOnAdd = refreshOnAdd,
            EffectType = effectType,
            Value = value,
            UIData = new UIBuffData
            {
                Id = buffId,
                Icon = string.IsNullOrWhiteSpace(iconPath)
                    ? null
                    : Resources.Load<Sprite>(iconPath),
                Stack = 1,
                NormalizedRemaining = 1f
            }
        };

        activeBuffs.Add(buffId, newBuff);

        ApplyAddedStack(newBuff);
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
        FindPlayerHealth();

        if (playerHealth == null)
        {
            return;
        }

        switch (buff.EffectType)
        {
            case "Shield":
                playerHealth.AddShield(buff.Value);
                break;

            default:
                Debug.LogWarning(
                    $"没有对应的 Buff 效果：{buff.EffectType}",
                    this
                );
                break;
        }
    }

    private void RemoveEffect(ActiveBuff buff)
    {
        FindPlayerHealth();

        if (playerHealth == null)
        {
            return;
        }

        switch (buff.EffectType)
        {
            case "Shield":
                playerHealth.ClearShield();
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

    private void FindPlayerHealth()
    {
        if (playerHealth == null)
        {
            GrayboxPlayerController player =
                FindObjectOfType<GrayboxPlayerController>();

            if (player != null)
            {
                playerHealth = player.GetComponent<Health>();
            }
        }

        BindPlayerHealthEvents(playerHealth);
    }

    private void BindPlayerHealthEvents(Health health)
    {
        if (subscribedPlayerHealth == health)
        {
            return;
        }

        if (subscribedPlayerHealth != null)
        {
            subscribedPlayerHealth.ShieldDepleted -=
                HandleShieldDepleted;
        }

        subscribedPlayerHealth = health;

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
