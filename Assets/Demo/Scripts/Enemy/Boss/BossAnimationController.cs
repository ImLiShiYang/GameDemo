using UnityEngine;
using UnityEngine.AI;

public class BossAnimationController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private NavMeshAgent agent;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int DeadHash = Animator.StringToHash("Dead");
    private static readonly int ChargingHash = Animator.StringToHash("IsCharging");
    private static readonly int SlamHash = Animator.StringToHash("Slam");
        
    

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }
    }

    private void Update()
    {
        if (animator == null || agent == null)
        {
            return;
        }

        float speed = agent.velocity.magnitude;
        animator.SetFloat(SpeedHash, speed, 0.1f, Time.deltaTime);
    }
    
    public void SetCharging(bool value)
    {
        animator.SetBool(ChargingHash, value);
    }
    
    public void SetSlam()
    {
        animator.SetTrigger(SlamHash);
    }
    
    public void PlayDead()
    {
        animator.ResetTrigger(AttackHash);
        animator.SetFloat(SpeedHash, 0f);
        animator.SetBool(DeadHash, true);
    }
    
    public void PlayAttack()
    {
        animator.SetTrigger(AttackHash);
    }
    
}