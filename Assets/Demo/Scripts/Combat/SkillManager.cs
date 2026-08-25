using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家技能系统入口。
/// 负责接收技能释放请求、读取 Lua 技能配置、
/// 管理技能冷却，后续负责具体技能执行。
/// </summary>
public class SkillManager : MonoBehaviour
{
    [SerializeField]
    private Transform player;

    [SerializeField] 
    private GrayboxPlayerController playerController;
    
    [SerializeField]
    private LayerMask enemyMask;
    
    [Header("Skill Warning")]
    [SerializeField] private GameObject shockWaveWarningPrefab;
    
    [Header("Piercing Beam")]
    [SerializeField] 
    private GameObject piercingBeamEffectPrefab;
    
    [SerializeField, Min(0.01f)] 
    private float piercingBeamEffectLifeTime = 0.2f;
    
    private const string SkillConfigModule ="Skill.SkillConfig";

    private int SkillTargetMask
    {
        get
        {
            int bossLayer = LayerMask.NameToLayer("Boss");
            return bossLayer < 0 ? enemyMask.value : enemyMask.value | (1 << bossLayer);
        }
    }
    
    private sealed class SkillRuntimeConfig
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string Executor;

        public float Damage;
        public float Range;
        public float Cooldown;
        public float WarningTime;
        public int InterruptPower;
    }

    private delegate void SkillExecutor(
        SkillRuntimeConfig config
    );

    private readonly Dictionary<string, SkillExecutor>skillExecutors =new Dictionary<string, SkillExecutor>();

    
    private readonly Dictionary<string, float> cooldownDurations = new Dictionary<string, float>();

    /// <summary>
    /// 每个技能下一次允许释放的时间。
    ///
    /// 例如：
    /// ShockWave -> 16.5
    ///
    /// 表示 Time.time 到达 16.5 之后，
    /// ShockWave 才能再次释放。
    /// </summary>
    private readonly Dictionary<string, float> nextCastTimes =new Dictionary<string, float>();


    private void Awake()
    {
        if (playerController == null && player != null)
        {
            playerController = player.GetComponent<GrayboxPlayerController>();
        }
        RegisterSkillExecutors();
    }

    private void RegisterSkillExecutors()
    {
        skillExecutors["ShockWave"] = ExecuteShockWaveSkill;
        skillExecutors["PiercingBeam"] = ExecutePiercingBeamSkill;
    }
    
    private void DetectEnemiesInRange(string skillId,float range)
    {
        if (player == null)
        {
            Debug.LogError(
                "SkillManager 没有设置 Player。",
                this
            );

            return;
        }

        Collider[] hitColliders =
            Physics.OverlapSphere(
                player.position,
                range,
                SkillTargetMask,
                QueryTriggerInteraction.Ignore
            );

        HashSet<IDamageable> detectedEnemies =
            new HashSet<IDamageable>();

        foreach (Collider hitCollider in hitColliders)
        {
            if (hitCollider == null)
            {
                continue;
            }

            IDamageable damageable =
                hitCollider.GetComponentInParent<IDamageable>();

            if (damageable == null ||
                damageable.IsDead)
            {
                continue;
            }

            detectedEnemies.Add(damageable);
        }

        Debug.Log(
            $"技能 {skillId} 范围检测完成：" +
            $"range = {range}，" +
            $"检测到 {detectedEnemies.Count} 个敌人。",
            this
        );

        foreach (IDamageable enemy in detectedEnemies)
        {
            if (enemy is Component component)
            {
                Debug.Log(
                    $"检测到敌人：{component.gameObject.name}",
                    component
                );
            }
        }
    }
    
    public void CastSkill(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            Debug.LogError(
                "SkillManager 收到的 SkillId 为空。",
                this
            );

            return;
        }

        if (!TryGetSkillConfig(skillId,out SkillRuntimeConfig config))
        {
            return;
        }

        if (!skillExecutors.TryGetValue(config.Executor,out SkillExecutor executor))
        {
            Debug.LogError(
                $"技能 {config.Id} 找不到执行器：" +
                $"{config.Executor}",
                this
            );

            return;
        }

        if (!CanCast(config.Id,out float remainingCooldown))
        {
            Debug.Log(
                $"技能 {config.DisplayName} 冷却中，" +
                $"剩余 {remainingCooldown:F2} 秒。",
                this
            );

            return;
        }

        StartCooldown(config.Id,config.Cooldown);

        executor(config);

        Debug.Log(
            $"释放技能：{config.DisplayName} | " +
            $"ID：{config.Id} | " +
            $"Executor：{config.Executor} | " +
            $"伤害：{config.Damage} | " +
            $"范围：{config.Range} | " +
            $"冷却：{config.Cooldown}",
            this
        );
    }
    
    private bool TryGetSkillConfig(
        string skillId,
        out SkillRuntimeConfig config)
    {
        config = null;

        LuaManager luaManager = GameEntry.Lua;

        if (luaManager == null)
        {
            Debug.LogError(
                "SkillManager 找不到 LuaManager。",
                this
            );

            return false;
        }

        object[] results =
            luaManager.CallWithResults(
                SkillConfigModule,
                "GetSkillValues",
                skillId
            );

        if (results == null ||
            results.Length < 9 ||
            results[0] == null)
        {
            Debug.LogError(
                $"读取技能配置失败：{skillId}",
                this
            );

            return false;
        }

        try
        {
            config = new SkillRuntimeConfig
            {
                Id =
                    Convert.ToString(results[0]),

                DisplayName =
                    Convert.ToString(results[1]),
                
                Description =
                    Convert.ToString(results[8]),

                Executor =
                    Convert.ToString(results[2]),

                Damage =
                    Convert.ToSingle(results[3]),

                Range =
                    Convert.ToSingle(results[4]),

                Cooldown =
                    Convert.ToSingle(results[5]),

                WarningTime =
                    Convert.ToSingle(results[6]),

                InterruptPower =
                    Mathf.Max(
                        0,
                        Convert.ToInt32(results[7])
                    )
            };

            return true;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"解析技能配置失败：{skillId}\n" +
                exception,
                this
            );

            config = null;

            return false;
        }
    }
    
    private void ExecuteShockWaveSkill(SkillRuntimeConfig config)
    {
        StartCoroutine(
            ExecuteShockWaveRoutine(
                config.Damage,
                config.Range,
                config.WarningTime,
                config.InterruptPower
            )
        );
    }

    private void ExecutePiercingBeamSkill(
        SkillRuntimeConfig config)
    {
        ExecutePiercingBeam(
            config.Damage,
            config.Range,
            config.InterruptPower
        );
    }

    
    private void ExecutePiercingBeam(
        float damage,
        float range,
        int interruptPower)
    {
        if (player == null || playerController == null)
        {
            Debug.LogError("PiercingBeam 缺少 Player 引用。", this);
            return;
        }

        if (!playerController.HasAimPoint)
        {
            Debug.LogWarning("PiercingBeam 当前没有有效瞄准点。", this);
            return;
        }

        Vector3 origin = playerController.AimOriginPosition;
        Vector3 direction = playerController.AimPoint - origin;

        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        direction.Normalize();
        
        SpawnPiercingBeamEffect(origin, direction, range);

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            range,
            SkillTargetMask,
            QueryTriggerInteraction.Ignore
        );

        HashSet<IDamageable> damagedEnemies = new HashSet<IDamageable>();

        foreach (RaycastHit hit in hits)
        {
            IDamageable damageable = hit.collider.GetComponentInParent<IDamageable>();

            if (damageable == null || damageable.IsDead)
            {
                continue;
            }

            if (!damagedEnemies.Add(damageable))
            {
                continue;
            }

            DamageInfo damageInfo = new DamageInfo(
                damage,
                player.gameObject,
                hit.point,
                direction,
                hit.normal,
                DamageKind.Skill,
                interruptPower
            );

            damageable.TakeDamage(damageInfo);

            if (damageable is Component component)
            {
                Debug.Log($"PiercingBeam 命中：{component.gameObject.name}，造成 {damage} 点伤害。", component);
            }
        }

        Debug.DrawRay(origin, direction * range, Color.cyan, 1f);

        Debug.Log($"PiercingBeam 释放完成，射程：{range}，命中敌人：{damagedEnemies.Count}", this);
    }
    
    private void SpawnPiercingBeamEffect(Vector3 origin, Vector3 direction, float range)
    {
        if (piercingBeamEffectPrefab == null)
        {
            return;
        }

        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);

        GameObject effect = Instantiate(
            piercingBeamEffectPrefab,
            origin,
            rotation
        );

        Destroy(effect, piercingBeamEffectLifeTime);
    }
    
    private IEnumerator ExecuteShockWaveRoutine(
        float damage,
        float range,
        float warningTime,
        int interruptPower)
    {
        if (player == null)
        {
            Debug.LogError("SkillManager 没有设置 Player。", this);
            yield break;
        }

        GameObject warningObject = CreateShockWaveWarning(range);

        if (warningTime > 0f)
        {
            yield return new WaitForSeconds(warningTime);
        }

        if (warningObject != null)
        {
            Destroy(warningObject);
        }

        ExecuteShockWave(damage, range, interruptPower);
    }
    
    private GameObject CreateShockWaveWarning(float range)
    {
        if (shockWaveWarningPrefab == null)
        {
            Debug.LogWarning("SkillManager 没有设置 ShockWave Warning Prefab。", this);
            return null;
        }

        GameObject warningObject = Instantiate(
            shockWaveWarningPrefab,
            player.position,
            Quaternion.identity
        );

        warningObject.transform.SetParent(player);

        float diameter = range * 2f;

        warningObject.transform.localScale = new Vector3(
            diameter,
            diameter,
            1f
        );

        return warningObject;
    }
    
    private void ExecuteShockWave(
        float damage,
        float range,
        int interruptPower)
    {
        if (player == null)
        {
            Debug.LogError(
                "SkillManager 没有设置 Player。",
                this
            );

            return;
        }

        Collider[] hitColliders =
            Physics.OverlapSphere(
                player.position,
                range,
                SkillTargetMask,
                QueryTriggerInteraction.Ignore
            );

        HashSet<IDamageable> damagedEnemies =
            new HashSet<IDamageable>();

        foreach (Collider hitCollider in hitColliders)
        {
            if (hitCollider == null)
            {
                continue;
            }

            IDamageable damageable =
                hitCollider.GetComponentInParent<IDamageable>();

            if (damageable == null ||
                damageable.IsDead)
            {
                continue;
            }

            // 同一个敌人可能有多个 Collider。
            // 已经造成过伤害就跳过。
            if (!damagedEnemies.Add(damageable))
            {
                continue;
            }

            Vector3 hitPoint =
                hitCollider.ClosestPoint(
                    player.position
                );

            Vector3 direction =
                hitCollider.bounds.center -
                player.position;

            if (direction.sqrMagnitude < 0.001f)
            {
                direction = player.forward;
            }
            else
            {
                direction.Normalize();
            }

            Vector3 hitNormal =
                -direction;

            DamageInfo damageInfo =
                new DamageInfo(
                    damage,
                    player.gameObject,
                    hitPoint,
                    direction,
                    hitNormal,
                    DamageKind.Skill,
                    interruptPower
                );

            damageable.TakeDamage(
                damageInfo
            );

            if (damageable is Component component)
            {
                Debug.Log(
                    $"ShockWave 命中：{component.gameObject.name}，" +
                    $"造成 {damage} 点伤害。",
                    component
                );
            }
        }

        Debug.Log(
            $"ShockWave 释放完成，" +
            $"范围：{range}，" +
            $"命中敌人：{damagedEnemies.Count}",
            this
        );
    }

    public string GetSkillDescription(string skillId)
    {
        if (TryGetSkillConfig(
                skillId,
                out SkillRuntimeConfig config))
        {
            return config.Description;
        }

        return string.Empty;
    }
    
    /// <summary>
    /// 判断技能当前是否可以释放。
    /// </summary>
    private bool CanCast(string skillId,out float remainingCooldown)
    {
        remainingCooldown = 0f;

        // 从来没有释放过这个技能。
        if (!nextCastTimes.TryGetValue(
                skillId,
                out float nextCastTime))
        {
            return true;
        }

        // 当前时间已经超过冷却结束时间。
        if (Time.time >= nextCastTime)
        {
            return true;
        }

        remainingCooldown =
            nextCastTime - Time.time;

        return false;
    }

    /// <summary>
    /// 让指定技能进入冷却。
    /// </summary>
    private void StartCooldown( string skillId,float cooldown)
    {
        float duration =
            Mathf.Max(0f, cooldown);

        cooldownDurations[skillId] =
            duration;

        nextCastTimes[skillId] =
            Time.time + duration;
    }
    
    public float GetRemainingCooldown(string skillId)
    {
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return 0f;
        }

        if (!nextCastTimes.TryGetValue( skillId,out float nextCastTime))
        {
            return 0f;
        }

        return Mathf.Max(
            0f,
            nextCastTime - Time.time
        );
    }
    
    public float GetCooldownNormalized(
        string skillId)
    {
        if (!cooldownDurations.TryGetValue(
                skillId,
                out float duration))
        {
            return 0f;
        }

        if (duration <= 0f)
        {
            return 0f;
        }

        float remaining =
            GetRemainingCooldown(skillId);

        return Mathf.Clamp01(
            remaining / duration
        );
    }
    
    public string GetSkillDisplayName(string skillId)
    {
        if (TryGetSkillConfig(
                skillId,
                out SkillRuntimeConfig config))
        {
            return config.DisplayName;
        }

        return skillId;
    }
    
}
