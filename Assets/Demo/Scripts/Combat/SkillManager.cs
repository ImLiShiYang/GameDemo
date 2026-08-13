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
                enemyMask,
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

        LuaManager luaManager = GameEntry.Lua;

        if (luaManager == null)
        {
            Debug.LogError(
                "SkillManager 找不到 LuaManager。",
                this
            );

            return;
        }

        // 1. 从 Lua 获取技能配置
        object[] results =luaManager.CallWithResults(SkillConfigModule,"GetSkillValues",skillId);

        if (results == null || results.Length < 5)
        {
            Debug.LogError(
                $"读取技能配置失败：{skillId}",
                this
            );

            return;
        }

        float damage =
            Convert.ToSingle(results[0]);

        float range =
            Convert.ToSingle(results[1]);

        float cooldown =
            Convert.ToSingle(results[2]);

        float warningTime =
            Convert.ToSingle(results[3]);

        int interruptPower =
            Mathf.Max(0, Convert.ToInt32(results[4]));

        // 2. 判断技能是否还在冷却
        if (!CanCast(skillId, out float remainingCooldown))
        {
            Debug.Log(
                $"技能 {skillId} 冷却中，" +
                $"剩余 {remainingCooldown:F2} 秒。",
                this
            );

            return;
        }

        // 3. 技能允许释放，开始进入冷却
        StartCooldown(skillId,cooldown);
        
        // DetectEnemiesInRange(skillId,range);
        
        ExecuteSkill(skillId,damage,range,warningTime,interruptPower);
        

        // 4. 目前先只打印，下一步再真正执行技能
        Debug.Log(
            $"释放技能：{skillId} | " +
            $"伤害：{damage} | " +
            $"范围：{range} | " +
            $"冷却：{cooldown} | " +
            $"预警：{warningTime} | " +
            $"打断力：{interruptPower}",
            this
        );
    }
    
    private void ExecuteSkill(
        string skillId,
        float damage,
        float range,
        float warningTime,
        int interruptPower)
    {
        switch (skillId)
        {
            case "ShockWave":
                StartCoroutine(ExecuteShockWaveRoutine(
                    damage,
                    range,
                    warningTime,
                    interruptPower));
                break;

            case "PiercingBeam":
                ExecutePiercingBeam(damage, range, interruptPower);
                break;

            default:
                Debug.LogWarning($"没有对应的技能执行逻辑：{skillId}", this);
                break;
        }
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
            enemyMask,
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
                enemyMask,
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
    private void StartCooldown(
        string skillId,
        float cooldown)
    {
        nextCastTimes[skillId] =
            Time.time + cooldown;
    }
}
