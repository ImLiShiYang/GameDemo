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
            health.Died += PlayDeath;
    }

    private void OnDisable()
    {
        if (health != null)
            health.Died -= PlayDeath;
    }

    private void Update()
    {
        if (isDead || animator == null)
            return;

        float speed = 0f;

        if (agent != null &&
            agent.enabled &&
            agent.isOnNavMesh)
        {
            speed = agent.velocity.magnitude;
        }

        animator.SetFloat(SpeedHash, speed);
    }

    public void PlayAttack()
    {
        if (isDead || animator == null)
            return;

        animator.SetTrigger(AttackHash);
    }

    public void PlayDeath()
    {
        if (isDead)
            return;

        isDead = true;

        // 先停止跑步参数，避免死亡动画和跑步动画竞争。
        animator.SetFloat(SpeedHash, 0f);
        animator.ResetTrigger(AttackHash);
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