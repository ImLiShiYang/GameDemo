using System;
using System.Collections;
using UnityEngine;

public class Health : MonoBehaviour, IDamageable
{
    [SerializeField]
    private float maxHealth = 100f;
    
    [SerializeField, Min(0f)]
    private float destroyDelay = 3f;

    private float currentHealth;
    private float currentShield;
    private float shieldCapacity;
    private GrayboxPlayerController playerController;
    private Coroutine returnToPoolRoutine;

    public bool IsDead { get; private set; }

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;
    public float CurrentShield => currentShield;
    public float ShieldCapacity => shieldCapacity;

    public event Action<DamageInfo> Damaged;
    public event Action Died;
    public event Action<float, float> ShieldChanged;
    public event Action ShieldDepleted;

    private void Awake()
    {
        currentHealth = maxHealth;

        // 小怪身上没有这个组件，因此只会对玩家生效。
        playerController = GetComponent<GrayboxPlayerController>();
    }
    public void ResetForReuse()
    {
        if (returnToPoolRoutine != null)
        {
            StopCoroutine(returnToPoolRoutine);
            returnToPoolRoutine = null;
        }

        IsDead = false;
        currentHealth = maxHealth;
        currentShield = 0f;
        shieldCapacity = 0f;

        NotifyShieldChanged();
    }
    
    public void AddShield(float amount)
    {
        amount = Mathf.Max(0f, amount);

        if (amount <= 0f)
        {
            return;
        }

        currentShield += amount;
        shieldCapacity += amount;

        NotifyShieldChanged();

        Debug.Log(
            $"获得护盾：+{amount}，当前护盾：{currentShield}",
            this
        );
    }

    public void ClearShield()
    {
        if (currentShield <= 0f && shieldCapacity <= 0f)
        {
            return;
        }

        currentShield = 0f;
        shieldCapacity = 0f;

        NotifyShieldChanged();

        Debug.Log("护盾已移除。", this);
    }


    public void TakeDamage(in DamageInfo damageInfo)
    {
        if (IsDead)
        {
            return;
        }

        // 玩家处于翻滚无敌帧时，不扣血，也不触发受击反馈。
        if (playerController != null &&playerController.IsInvincible)
        {
            Debug.Log("玩家处于翻滚无敌帧，本次伤害被免疫。", this);
            return;
        }

        float damage = Mathf.Max(0f, damageInfo.Amount);

        if (damage <= 0f)
        {
            return;
        }
        
        if (currentShield > 0f)
        {
            float absorbedDamage = Mathf.Min(currentShield, damage);

            currentShield -= absorbedDamage;
            damage -= absorbedDamage;

            bool shieldWasDepleted = currentShield <= 0f;

            NotifyShieldChanged();

            if (shieldWasDepleted)
            {
                ShieldDepleted?.Invoke();
            }

            Debug.Log(
                $"护盾吸收 {absorbedDamage} 点伤害，剩余护盾：{currentShield}",
                this
            );

            if (damage <= 0f)
            {
                damage = 0;
                return;
            }
        }

        currentHealth = Mathf.Max(0f,currentHealth - damage );

        DamageInfo actualDamageInfo = new DamageInfo(
            damage,
            damageInfo.Source,
            damageInfo.HitPoint,
            damageInfo.HitDirection,
            damageInfo.HitNormal,
            damageInfo.Kind,
            damageInfo.InterruptPower
        );
        
        Damaged?.Invoke(actualDamageInfo);

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void NotifyShieldChanged()
    {
        ShieldChanged?.Invoke(
            currentShield,
            shieldCapacity
        );
    }

    private void Die()
    {
        if (IsDead)
        {
            return;
        }

        IsDead = true;
        Died?.Invoke();

        PooledObject pooledObject = GetComponent<PooledObject>();

        if (pooledObject == null)
        {
            Destroy(gameObject, destroyDelay);
            return;
        }

        if (destroyDelay <= 0f)
        {
            pooledObject.Release();
            return;
        }

        returnToPoolRoutine = StartCoroutine(ReturnToPoolAfterDelay(pooledObject));
    }

    private IEnumerator ReturnToPoolAfterDelay(PooledObject pooledObject)
    {
        yield return new WaitForSeconds(destroyDelay);

        returnToPoolRoutine = null;

        if (pooledObject != null && !pooledObject.IsInPool)
        {
            pooledObject.Release();
        }
    }
}
