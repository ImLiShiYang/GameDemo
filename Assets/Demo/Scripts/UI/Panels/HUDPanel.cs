using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

public enum HUDSkillSlot
{
    Roll,
    PrimarySkill,
    SecondarySkill
}

public class HUDOpenArgs
{
    public Health PlayerHealth;
    public PlayerExperience PlayerExperience;
}

public class UIBuffData
{
    public string Id;
    public Sprite Icon;
    public int Stack = 1;

    /// <summary>
    /// 0 = 即将结束，1 = 剩余完整时长。
    /// </summary>
    public float NormalizedRemaining = 1f;
}

/// <summary>
/// 战斗 HUD。
///
/// 当前直接绑定：
/// Health
/// PlayerExperience
///
/// 另外提供：
/// Roll/Q/E 冷却显示
/// 波次显示
/// Buff 显示
/// Boss 血条
/// </summary>
public class HUDPanel : UIBase
{
    [Header("Player Health")]
    [SerializeField]
    private Slider healthSlider;

    [SerializeField]
    private TMP_Text healthText;

    [Header("Player Shield")]
    [SerializeField]
    private GameObject shieldRoot;

    [SerializeField]
    private Slider shieldSlider;

    [SerializeField]
    private TMP_Text shieldText;

    [Header("Experience")]
    [SerializeField]
    private Slider experienceSlider;

    [SerializeField]
    private TMP_Text levelText;

    [SerializeField]
    private TMP_Text experienceText;

    [Header("Cooldown - Image Type = Filled")]
    [SerializeField]
    private Image rollCooldownFill;

    [FormerlySerializedAs("qCooldownFill")]
    [SerializeField]
    private Image primarySkillCooldownFill;

    [FormerlySerializedAs("eCooldownFill")]
    [SerializeField]
    private Image secondarySkillCooldownFill;
    
    [Header("Skill Key Text")]
    [SerializeField]
    private TMP_Text primarySkillKeyText;

    [SerializeField]
    private TMP_Text secondarySkillKeyText;
    
    [Header("Skill Cooldown Text")]
    [SerializeField] 
    private TMP_Text primarySkillCooldownText;
    [SerializeField] 
    private TMP_Text secondarySkillCooldownText;

    private SkillManager skillManager;

    [Header("Wave")]
    [SerializeField]
    private TMP_Text waveText;

    [SerializeField]
    private TMP_Text enemyCountText;

    [Header("Boss")]
    [SerializeField]
    private GameObject bossRoot;

    [SerializeField]
    private TMP_Text bossNameText;

    [SerializeField]
    private Slider bossHealthSlider;

    [SerializeField]
    private TMP_Text bossHealthText;

    [Header("Buff")]
    [SerializeField]
    private Transform buffRoot;

    [SerializeField]
    private UIBuffItem buffItemPrefab;

    private Health playerHealth;
    private PlayerExperience playerExperience;

    private WaveManager waveManager;
    private Health bossHealth;

    private readonly Dictionary<string, UIBuffItem> buffItems =
        new Dictionary<string, UIBuffItem>();

    protected override void OnInit()
    {
        if (bossRoot != null)
        {
            bossRoot.SetActive(false);
        }

        SetShield(0f, 0f);

        SetCooldown(
            HUDSkillSlot.Roll,
            0f
        );

        SetCooldown(
            HUDSkillSlot.PrimarySkill,
            0f
        );

        SetCooldown(
            HUDSkillSlot.SecondarySkill,
            0f
        );

        if (primarySkillKeyText != null)
        {
            primarySkillKeyText.text =
                PlayerSkillInput.PrimarySkillKeyText;
        }

        if (secondarySkillKeyText != null)
        {
            secondarySkillKeyText.text =
                PlayerSkillInput.SecondarySkillKeyText;
        }
        
        SetSkillCooldownText(primarySkillCooldownText, 0f);
        SetSkillCooldownText(secondarySkillCooldownText, 0f);

        SetWave(0, 0);
        SetEnemyCount(0);
    }
    
    private void Update()
    {
        RefreshSkillCooldowns();
    }
    
    private void RefreshSkillCooldowns()
    {
        if (skillManager == null)
        {
            skillManager = GameEntry.Skill;
        }

        if (skillManager == null)
        {
            SetSkillCooldownText(primarySkillCooldownText, 0f);
            SetSkillCooldownText(secondarySkillCooldownText, 0f);
            return;
        }

        float primaryRemaining = skillManager.GetRemainingCooldown(PlayerSkillInput.PrimarySkillId);
        float secondaryRemaining = skillManager.GetRemainingCooldown(PlayerSkillInput.SecondarySkillId);

        SetSkillCooldownText(primarySkillCooldownText, primaryRemaining);
        SetSkillCooldownText(secondarySkillCooldownText, secondaryRemaining);
    }
    
    private void SetSkillCooldownText(TMP_Text target, float remaining)
    {
        if (target == null)
        {
            return;
        }

        if (remaining <= 0f)
        {
            target.text = "";
            return;
        }

        target.text = Mathf.CeilToInt(remaining).ToString();
    }

    protected override void OnOpen(object args)
    {
        BindFromArgs(args);
        BindWave();
    }

    protected override void OnRefresh(object args)
    {
        BindFromArgs(args);
        BindWave();
    }

    protected override void OnClose()
    {
        UnbindPlayer();
        UnbindBoss();
        UnbindWave();
    }

    private void BindWave()
    {
        // 防止重复绑定。
        UnbindWave();

        waveManager = GameEntry.Wave;

        if (waveManager == null)
        {
            SetWave(0, 0);
            SetEnemyCount(0);
            return;
        }

        // 监听“新波次开始”。
        waveManager.WaveStarted +=
            HandleWaveStarted;

        // 监听“当前存活敌人数变化”。
        waveManager.AliveEnemyCountChanged +=
            HandleAliveEnemyCountChanged;

        waveManager.BossSpawned += HandleBossSpawned;
        
        // HUD刚打开时立即同步一次当前状态。
        SetWave(
            waveManager.CurrentWaveNumber,
            waveManager.TotalWaveCount
        );

        SetEnemyCount(
            waveManager.AliveEnemyCount
        );
    }

    private void UnbindWave()
    {
        if (waveManager == null)
        {
            return;
        }

        waveManager.WaveStarted -=
            HandleWaveStarted;

        waveManager.AliveEnemyCountChanged -=
            HandleAliveEnemyCountChanged;

        waveManager.BossSpawned -= HandleBossSpawned;
        
        waveManager = null;
    }

    private void HandleWaveStarted(
        int currentWave,
        int totalWave)
    {
        SetWave(
            currentWave,
            totalWave
        );
    }

    private void HandleBossSpawned(string bossName, Health health)
    {
        BindBoss(bossName, health);
    }
    
    private void HandleAliveEnemyCountChanged(
        int aliveCount)
    {
        SetEnemyCount(aliveCount);
    }

    private void BindFromArgs(object args)
    {
        HUDOpenArgs openArgs = args as HUDOpenArgs;

        if (openArgs == null)
        {
            RefreshPlayerHealth();
            RefreshExperience();
            return;
        }

        BindPlayer(
            openArgs.PlayerHealth,
            openArgs.PlayerExperience
        );
    }

    public void BindPlayer(
        Health health,
        PlayerExperience experience)
    {
        UnbindPlayer();

        playerHealth = health;
        playerExperience = experience;

        if (playerHealth != null)
        {
            playerHealth.Damaged += HandlePlayerDamaged;
            playerHealth.Died += HandlePlayerDied;
            playerHealth.ShieldChanged += HandleShieldChanged;
        }

        if (playerExperience != null)
        {
            playerExperience.ExperienceChanged +=
                HandleExperienceChanged;

            playerExperience.LeveledUp +=
                HandleLevelUp;
        }

        RefreshPlayerHealth();
        RefreshPlayerShield();
        RefreshExperience();
    }

    private void UnbindPlayer()
    {
        if (playerHealth != null)
        {
            playerHealth.Damaged -= HandlePlayerDamaged;
            playerHealth.Died -= HandlePlayerDied;
            playerHealth.ShieldChanged -= HandleShieldChanged;
        }

        if (playerExperience != null)
        {
            playerExperience.ExperienceChanged -=
                HandleExperienceChanged;

            playerExperience.LeveledUp -=
                HandleLevelUp;
        }

        playerHealth = null;
        playerExperience = null;
    }

    private void HandlePlayerDamaged(
        DamageInfo damageInfo)
    {
        RefreshPlayerHealth();
    }

    private void HandlePlayerDied()
    {
        RefreshPlayerHealth();
    }

    private void HandleShieldChanged(
        float current,
        float capacity)
    {
        SetShield(current, capacity);
    }

    private void RefreshPlayerHealth()
    {
        if (playerHealth == null)
        {
            if (healthSlider != null)
            {
                healthSlider.value = 0f;
            }

            if (healthText != null)
            {
                healthText.text = "-- / --";
            }

            return;
        }

        float current =
            playerHealth.CurrentHealth;

        float max =
            playerHealth.MaxHealth;

        if (healthSlider != null)
        {
            healthSlider.value =
                max > 0f
                    ? current / max
                    : 0f;
        }

        if (healthText != null)
        {
            healthText.text =
                $"{Mathf.CeilToInt(current)} / " +
                $"{Mathf.CeilToInt(max)}";
        }
    }

    private void RefreshPlayerShield()
    {
        if (playerHealth == null)
        {
            SetShield(0f, 0f);
            return;
        }

        SetShield(
            playerHealth.CurrentShield,
            playerHealth.ShieldCapacity
        );
    }

    private void SetShield(float current, float capacity)
    {
        current = Mathf.Max(0f, current);
        capacity = Mathf.Max(0f, capacity);

        bool visible = current > 0f && capacity > 0f;

        if (shieldRoot != null)
        {
            shieldRoot.SetActive(visible);
        }

        if (shieldSlider != null)
        {
            shieldSlider.minValue = 0f;
            shieldSlider.maxValue = Mathf.Max(1f, capacity);
            shieldSlider.value = current;
        }

        if (shieldText != null)
        {
            shieldText.text = visible
                ? $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(capacity)}"
                : string.Empty;
        }
    }

    private void HandleExperienceChanged(
        int current,
        int required)
    {
        RefreshExperience();
    }

    private void HandleLevelUp(int newLevel)
    {
        RefreshExperience();
    }

    private void RefreshExperience()
    {
        if (playerExperience == null)
        {
            if (experienceSlider != null)
            {
                experienceSlider.value = 0f;
            }

            if (levelText != null)
            {
                levelText.text = "Lv.--";
            }

            if (experienceText != null)
            {
                experienceText.text = "-- / --";
            }

            return;
        }

        int current =
            playerExperience.CurrentExperience;

        int required =
            playerExperience.ExperienceToNextLevel;

        int level =
            playerExperience.Level;

        if (experienceSlider != null)
        {
            experienceSlider.value =
                required > 0
                    ? (float)current / required
                    : 0f;
        }

        if (levelText != null)
        {
            levelText.text =
                $"Lv.{level}";
        }

        if (experienceText != null)
        {
            experienceText.text =
                $"{current} / {required}";
        }
    }

    /// <summary>
    /// normalizedRemaining：
    /// 1 = 刚进入冷却
    /// 0 = 冷却结束
    /// </summary>
    public void SetCooldown(
        HUDSkillSlot slot,
        float normalizedRemaining)
    {
        float value =
            Mathf.Clamp01(normalizedRemaining);

        Image target = null;

        switch (slot)
        {
            case HUDSkillSlot.Roll:
                target = rollCooldownFill;
                break;

            case HUDSkillSlot.PrimarySkill:
                target =
                    primarySkillCooldownFill;
                break;

            case HUDSkillSlot.SecondarySkill:
                target =
                    secondarySkillCooldownFill;
                break;
        }

        if (target != null)
        {
            target.fillAmount = value;
        }
    }

    private void SetEnemyCount(
        int aliveCount)
    {
        if (enemyCountText == null)
        {
            return;
        }

        enemyCountText.text =
            $"存活敌人：{Mathf.Max(0, aliveCount)}";
    }

    public void SetWave(
        int currentWave,
        int totalWave)
    {
        if (waveText == null)
        {
            return;
        }

        if (currentWave <= 0)
        {
            waveText.text = "波次 --";
            return;
        }

        if (totalWave > 0)
        {
            waveText.text =
                $"第 {currentWave} / {totalWave} 波";
        }
        else
        {
            waveText.text =
                $"第 {currentWave} 波";
        }
    }

    public void BindBoss(string bossName,Health health)
    {
        UnbindBoss();

        bossHealth = health;

        if (bossNameText != null)
        {
            bossNameText.text =
                string.IsNullOrWhiteSpace(bossName)
                    ? "BOSS"
                    : bossName;
        }

        if (bossRoot != null)
        {
            bossRoot.SetActive(
                bossHealth != null
            );
        }

        if (bossHealth != null)
        {
            bossHealth.Damaged += HandleBossDamaged;
            bossHealth.Died += HandleBossDied;
        }

        RefreshBossHealth();
    }

    public void UnbindBoss()
    {
        if (bossHealth != null)
        {
            bossHealth.Damaged -= HandleBossDamaged;
            bossHealth.Died -= HandleBossDied;
        }

        bossHealth = null;

        if (bossRoot != null)
        {
            bossRoot.SetActive(false);
        }
    }

    private void HandleBossDamaged(
        DamageInfo damageInfo)
    {
        RefreshBossHealth();
    }

    private void HandleBossDied()
    {
        RefreshBossHealth();

        if (bossRoot != null)
        {
            bossRoot.SetActive(false);
        }
    }

    private void RefreshBossHealth()
    {
        if (bossHealth == null)
        {
            return;
        }

        float current =
            bossHealth.CurrentHealth;

        float max =
            bossHealth.MaxHealth;

        if (bossHealthSlider != null)
        {
            bossHealthSlider.value =
                max > 0f
                    ? current / max
                    : 0f;
        }

        if (bossHealthText != null)
        {
            bossHealthText.text =
                $"{Mathf.CeilToInt(current)} / " +
                $"{Mathf.CeilToInt(max)}";
        }
    }

    public void SetBuff(UIBuffData data)
    {
        if (data == null ||
            string.IsNullOrEmpty(data.Id) ||
            buffRoot == null ||
            buffItemPrefab == null)
        {
            return;
        }

        if (!buffItems.TryGetValue(data.Id,out UIBuffItem item) || item == null)
        {
            item = Instantiate(
                buffItemPrefab,
                buffRoot,
                false
            );

            buffItems[data.Id] = item;
        }

        item.gameObject.SetActive(true);

        item.SetData(
            data.Icon,
            Mathf.Max(1, data.Stack),
            data.NormalizedRemaining
        );
    }

    public void RemoveBuff(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        if (!buffItems.TryGetValue(
                id,
                out UIBuffItem item))
        {
            return;
        }

        buffItems.Remove(id);

        if (item != null)
        {
            Destroy(item.gameObject);
        }
    }

    public void ClearBuffs()
    {
        foreach (UIBuffItem item in buffItems.Values)
        {
            if (item != null)
            {
                Destroy(item.gameObject);
            }
        }

        buffItems.Clear();
    }
}
