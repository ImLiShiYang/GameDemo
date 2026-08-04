using System;
using UnityEngine;

public class Health : MonoBehaviour, IDamageable
{
    [SerializeField]
    private float maxHealth = 100f;

    private float currentHealth;

    public bool IsDead { get; private set; }

    public float CurrentHealth => currentHealth;
    public float MaxHealth => maxHealth;

    public event Action<DamageInfo> Damaged;
    public event Action Died;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    public void TakeDamage(in DamageInfo damageInfo)
    {
        if (IsDead)
        {
            return;
        }

        float damage = Mathf.Max(0f, damageInfo.Amount);
        currentHealth = Mathf.Max(0f, currentHealth - damage);

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

        Destroy(gameObject);
    }
}