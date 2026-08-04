using UnityEngine;
using UnityEngine.InputSystem;

public class DamageTester : MonoBehaviour
{
    [SerializeField]
    private Health targetHealth;

    [SerializeField]
    private float testDamage = 10f;

    private void OnEnable()
    {
        if (targetHealth == null)
        {
            return;
        }

        targetHealth.Damaged += OnTargetDamaged;
        targetHealth.Died += OnTargetDied;
    }

    private void OnDisable()
    {
        if (targetHealth == null)
        {
            return;
        }

        targetHealth.Damaged -= OnTargetDamaged;
        targetHealth.Died -= OnTargetDied;
    }

    private void Update()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        if (Keyboard.current.tKey.wasPressedThisFrame)
        {
            TestDamage();
        }
    }

    private void TestDamage()
    {
        if (targetHealth == null)
        {
            Debug.LogError("DamageTester 没有设置 Target Health。");
            return;
        }

        if (targetHealth.IsDead)
        {
            Debug.Log("目标已经死亡。");
            return;
        }

        DamageInfo damageInfo = new DamageInfo(
            testDamage,
            gameObject,
            targetHealth.transform.position,
            targetHealth.transform.forward);

        // 故意使用接口类型调用，用来验证 IDamageable。
        IDamageable damageable = targetHealth;
        damageable.TakeDamage(damageInfo);

        Debug.Log(
            $"测试伤害：{testDamage}，" +
            $"剩余血量：{targetHealth.CurrentHealth}/{targetHealth.MaxHealth}");
    }

    private void OnTargetDamaged(DamageInfo damageInfo)
    {
        Debug.Log(
            $"收到受伤事件，伤害来源：{damageInfo.Source.name}，" +
            $"伤害值：{damageInfo.Amount}");
    }

    private void OnTargetDied()
    {
        Debug.Log("收到死亡事件：测试目标死亡。");
    }
}