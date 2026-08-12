using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(Health))]
public class EnemyAnimationController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent agent;

    private static readonly int SpeedHash =
        Animator.StringToHash("Speed");

    private static readonly int AttackHash =
        Animator.StringToHash("Attack");

    private static readonly int HitHash =
        Animator.StringToHash("Hit");

    private static readonly int DeadHash =
        Animator.StringToHash("Dead");

    private Health health;
    private bool isDead;

    private void Awake()
    {
        health = GetComponent<Health>();

        if (animator == null)
            animator = GetComponent<Animator>();

        if (agent == null)
            agent = GetComponent<NavMeshAgent>();
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Damaged += PlayHit;
            health.Died += PlayDeath;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Damaged -= PlayHit;
            health.Died -= PlayDeath;
        }
    }
    
    

    private void PlayHit(DamageInfo damageInfo)
    {
        // 致命伤害交给死亡动画处理。
        if (isDead || health.CurrentHealth <= 0f || animator == null)
            return;

        AnimatorStateInfo currentState =
            animator.GetCurrentAnimatorStateInfo(0);

        bool isEnteringAttack =
            animator.IsInTransition(0) &&
            animator.GetNextAnimatorStateInfo(0)
                .IsName("Attack");

        bool isPlayingAttack =
            currentState.IsName("Attack");

        /*
         * 攻击霸体：
         * 已经进入攻击状态或正在切入攻击状态时，
         * 仍然受到伤害，但不播放 Hit，也不取消攻击。
         */
        if (isPlayingAttack || isEnteringAttack)
        {
            return;
        }
        
        animator.ResetTrigger(AttackHash);
        animator.ResetTrigger(HitHash);
        animator.SetTrigger(HitHash);
    }
    public void ResetForReuse()
    {
        isDead = false;
        Collider[] colliders = GetComponentsInChildren<Collider>(true);

        foreach (Collider enemyCollider in colliders)
        {
            enemyCollider.enabled = true;
        }

        if (animator == null)
        {
            return;
        }

        animator.Rebind();
        animator.Update(0f);
        animator.SetFloat(SpeedHash, 0f);
        animator.ResetTrigger(AttackHash);
        animator.ResetTrigger(HitHash);
        animator.SetBool(DeadHash, false);
    }


    public void PlayDeath()
    {
        if (isDead)
            return;

        isDead = true;

        // 先停止跑步参数，避免死亡动画和跑步动画竞争。
        animator.SetFloat(SpeedHash, 0f);
        animator.ResetTrigger(AttackHash);
        animator.ResetTrigger(HitHash);
        animator.SetBool(DeadHash, true);

        // 停止 NavMeshAgent 移动。
        if (agent != null &&
            agent.enabled &&
            agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        // 防止死亡后继续被子弹命中。
        Collider[] colliders = GetComponentsInChildren<Collider>();

        foreach (Collider enemyCollider in colliders)
        {
            enemyCollider.enabled = false;
        }
    }
}