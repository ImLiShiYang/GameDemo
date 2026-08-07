using System;
using UnityEngine;

public class Health : MonoBehaviour, IDamageable
{
    [SerializeField]
    private float maxHealth = 100f;
    
    [SerializeField, Min(0f)]
    private float destroyDelay = 3f;

    private float currentHealth;
    private GrayboxPlayerController playerController;

    public bool IsDead { get; private set; }

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    public event Action<DamageInfo> Damaged;
    public event Action Died;

    private void Awake()
    {
        currentHealth = maxHealth;

        // 小怪身上没有这个组件，因此只会对玩家生效。
        playerController = GetComponent<GrayboxPlayerController>();
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

        currentHealth = Mathf.Max(0f,currentHealth - damage );

        Damaged?.Invoke(damageInfo);

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    private void Die()
    {
        if (IsDead)
        {
            return;
        }

        IsDead = true;
        Died?.Invoke();

        Destroy(gameObject, destroyDelay);
    }
}