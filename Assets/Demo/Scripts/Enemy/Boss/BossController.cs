using UnityEngine;
using UnityEngine.AI;

public class BossController : MonoBehaviour
{
    private enum BossState
    {
        Chase,
        Attack,
        //冲锋
        Charge,
        Dead
    }
    
    // 冲锋内部分为三个阶段
    private enum ChargePhase
    {
        // 蓄力阶段：Boss 停止移动，给玩家躲避时间
        Windup,

        // 冲锋阶段：Boss 沿进入冲锋时锁定的方向高速移动
        Moving,

        // 恢复阶段：冲锋结束后短暂停顿，再恢复正常追击
        Recover
    }

    [Header("References")]
    [SerializeField] private NavMeshAgent agent;
    [SerializeField] private Animator animator;
    [SerializeField] private Transform player;
    [SerializeField] private Health health;

    [Header("Movement")]
    [SerializeField] private float walkSpeed = 1.5f;
    [SerializeField] private float runSpeed = 3.5f;
    [SerializeField] private float walkDistance = 6f;

    [Header("Attack")]
    [SerializeField] private float attackDistance = 2.5f;
    [SerializeField] private float attackCooldown = 1.5f;
    [SerializeField] private float attackRotationSpeed = 720f;
    [SerializeField] private float attackFacingAngle = 5f;
    [SerializeField] private float attackDamage = 30f;
    [SerializeField] private float attackHitTolerance = 0.5f;
    
    [Header("Charge")]
    // 玩家至少距离 Boss 多远时，才允许使用冲锋。
    // 防止玩家贴脸时 Boss 还发动冲锋。
    [SerializeField] private float chargeMinDistance = 4f;

    // 玩家距离 Boss 超过这个距离时，不发动冲锋。
    // 只有在 chargeMinDistance ~ chargeMaxDistance 范围内才会触发。
    [SerializeField] private float chargeMaxDistance = 10f;

    // 两次冲锋之间的冷却时间。
    [SerializeField] private float chargeCooldown = 6f;

    // 冲锋前的蓄力时间。
    // 这段时间 Boss 停止移动，之后还会用来显示红色预警区域。
    [SerializeField] private float chargeWindupDuration = 0.8f;

    // Boss 真正开始冲锋后的移动速度。
    [SerializeField] private float chargeSpeed = 12f;

    // 冲锋终点额外向前延伸的距离。
    // 让 Boss 不只是冲到玩家锁定时的位置，而是稍微冲过去一点。
    [SerializeField] private float chargeExtraDistance = 1.5f;

    // 冲锋结束后的恢复/硬直时间。
    // 这段时间结束以后 Boss 才重新进入 Chase。
    [SerializeField] private float chargeRecoverDuration = 0.5f;
    
    // 冲锋撞到玩家时造成的伤害。
    [SerializeField] private float chargeDamage = 40f;

    // Boss 与玩家距离小于这个值时，认为冲锋撞到了玩家。
    [SerializeField] private float chargeHitDistance = 1.5f;

    [Header("Animator")] [SerializeField] private BossAnimationController bossAnimationController;
    [SerializeField] private string attackParameter = "Attack";
    [SerializeField] private string attackStateName = "Attack1";
    [SerializeField] private string deadParameter = "Dead";
    [SerializeField] private string deadStateName = "Dead";
    [SerializeField, Min(0f)] private float deathTransitionDuration = 0.1f;

    private BossState currentState;

    private float nextAttackTime;

    private bool attackActive;
    private bool attackAnimationEntered;
    private bool attackCommitted;

    private int attackHash;
    private int attackStateHash;
    private int deadHash;
    
    // 当前冲锋处于哪个阶段：蓄力、移动、恢复。
    private ChargePhase chargePhase;

    // Boss 开始冲锋时锁定的方向。
    // 一旦锁定，冲锋过程中不会再跟踪玩家。
    private Vector3 chargeDirection;

    // 当前还剩多少距离没有冲完。
    // 每一帧移动以后都会从这里扣除已经移动的距离。
    private float chargeRemainingDistance;

    // 当前冲锋阶段结束的时间。
    // Windup 和 Recover 都通过这个时间判断什么时候结束。
    private float chargePhaseEndTime;

    // 下一次允许使用冲锋的时间。
    private float nextChargeTime;
    
    // 当前这一轮冲锋是否已经撞到过玩家。
    // 防止冲锋经过玩家身体的几帧内连续造成多次伤害。
    private bool chargeHasHitPlayer;

    private void Awake()
    {
        if (agent == null)
        {
            agent = GetComponent<NavMeshAgent>();
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        
        if (health == null)
        {
            health = GetComponent<Health>();
        }

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        attackHash = Animator.StringToHash(attackParameter);
        attackStateHash = Animator.StringToHash(attackStateName);
        deadHash = Animator.StringToHash(deadParameter);
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Died += HandleDied;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= HandleDied;
        }
    }
    
    private void HandleDied()
    {
        currentState = BossState.Dead;
        ResetAttackRuntime();

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        if (bossAnimationController == null)
        {
            Debug.LogError("bossAnimationController为空");
            return;
        }
        bossAnimationController.PlayDead();
    }
    
    private void Start()
    {
        EnterChaseState();
    }

    private void Update()
    {
        if (health != null && health.IsDead)
        {
            return;
        }
        
        if (agent == null || animator == null || player == null)
        {
            return;
        }

        float distanceToPlayer = GetFlatDistance(
            transform.position,
            player.position
        );

        switch (currentState)
        {
            case BossState.Chase:
                UpdateChase(distanceToPlayer);
                break;

            case BossState.Attack:
                UpdateAttack(distanceToPlayer);
                break;
            
            case BossState.Charge:
                UpdateCharge();
                break;
        }
    }

    private void UpdateChase(float distanceToPlayer)
    {
        // 玩家已经进入普通攻击范围，优先进行普通攻击。
        if (distanceToPlayer <= attackDistance)
        {
            EnterAttackState();
            return;
        }

        // 冲锋冷却结束，并且玩家处于适合冲锋的距离范围内，
        // 就停止普通追击，进入 Charge 状态。
        if (Time.time >= nextChargeTime && distanceToPlayer >= chargeMinDistance && distanceToPlayer <= chargeMaxDistance)
        {
            EnterChargeState();
            return;
        }

        // 当前既不能普通攻击，也不满足冲锋条件，继续追击玩家。
        agent.isStopped = false;

        if (distanceToPlayer > walkDistance)
        {
            agent.speed = runSpeed;
        }
        else
        {
            agent.speed = walkSpeed;
        }

        agent.SetDestination(player.position);
    }
    
    private void EnterChargeState()
    {
        // Boss 主状态切换成冲锋。
        currentState = BossState.Charge;

        // 停止 NavMeshAgent 原本的追击。
        // 冲锋期间不再使用 SetDestination 追踪玩家。
        agent.isStopped = true;
        agent.ResetPath();

        // 记录“进入冲锋这一刻”玩家所在的方向。
        Vector3 direction = player.position - transform.position;

        // 冲锋只发生在水平面，忽略玩家和 Boss 的高度差。
        direction.y = 0f;

        // Boss 和玩家位置几乎重合时无法得到有效方向，
        // 取消本次冲锋，重新回到 Chase。
        if (direction.sqrMagnitude < 0.001f)
        {
            EnterChaseState();
            return;
        }

        // 记录此刻玩家距离。
        float distance = direction.magnitude;

        // 锁定冲锋方向。
        // 后续冲锋过程中不会再次读取玩家位置修改这个方向，
        // 所以玩家可以通过横向移动躲开冲锋。
        chargeDirection = direction.normalized;

        // 冲锋距离 = 玩家当前距离 + 一段额外距离。
        // 这样 Boss 会冲过玩家原来的位置，而不是刚好停在那里。
        chargeRemainingDistance = distance + chargeExtraDistance;
        
        // 新的一轮冲锋还没有撞到玩家。
        chargeHasHitPlayer = false;

        // Boss 在蓄力开始时直接面对即将冲锋的方向。
        transform.rotation = Quaternion.LookRotation(chargeDirection, Vector3.up);

        // 首先进入冲锋的蓄力阶段。
        chargePhase = ChargePhase.Windup;

        // 记录蓄力结束时间。
        chargePhaseEndTime = Time.time + chargeWindupDuration;

        // 从这一刻开始计算下一次冲锋冷却。
        nextChargeTime = Time.time + chargeCooldown;

        Debug.Log($"{name} 开始蓄力冲锋，锁定方向：{chargeDirection}", this);
    }
    
    private void UpdateCharge()
    {
        // Charge 是 Boss 的一个大状态，
        // 内部再通过 ChargePhase 控制：
        //
        // Windup
        // ↓
        // Moving
        // ↓
        // Recover
        // ↓
        // Chase
        switch (chargePhase)
        {
            case ChargePhase.Windup:
                UpdateChargeWindup();
                break;

            case ChargePhase.Moving:
                UpdateChargeMoving();
                break;

            case ChargePhase.Recover:
                UpdateChargeRecover();
                break;
        }
    }
    
    private void UpdateChargeWindup()
    {
        // 蓄力期间 Boss 完全停止移动。
        // 后面红色冲锋预警区域也会在这个阶段显示。
        agent.isStopped = true;

        // 还没有到蓄力结束时间就继续等待。
        if (Time.time < chargePhaseEndTime)
        {
            return;
        }

        // 0.8 秒蓄力结束，正式开始冲锋。
        chargePhase = ChargePhase.Moving;
    }
    
    private void UpdateChargeMoving()
    {
        // 计算这一帧 Boss 应该移动多少距离。
        // 例如 chargeSpeed = 12，60 FPS 时，
        // 每帧大约移动 12 / 60 = 0.2 米。
        float moveDistance = chargeSpeed * Time.deltaTime;

        // 沿之前锁定好的 chargeDirection 移动。
        // 这里没有重新读取 player.position，
        // 因此 Boss 冲锋过程中不会跟踪玩家转弯。
        agent.Move(chargeDirection * moveDistance);
        
        // 冲锋移动过程中检查是否撞到玩家。
        TryDealChargeDamage();

        // 从剩余冲锋距离中扣掉这一帧已经移动的距离。
        chargeRemainingDistance -= moveDistance;

        // 还有剩余距离就继续冲锋。
        if (chargeRemainingDistance > 0f)
        {
            return;
        }

        // 冲锋距离已经跑完，进入恢复/硬直阶段。
        chargePhase = ChargePhase.Recover;

        // 记录恢复阶段结束时间。
        chargePhaseEndTime = Time.time + chargeRecoverDuration;
    }
    
    private void TryDealChargeDamage()
    {
        // 本次冲锋已经命中过玩家，就不再重复造成伤害。
        if (chargeHasHitPlayer || player == null)
        {
            return;
        }

        // 计算 Boss 和玩家在水平面上的距离。
        float distance = GetFlatDistance(transform.position, player.position);

        // 距离还没有达到冲锋命中范围。
        if (distance > chargeHitDistance)
        {
            return;
        }

        // 查找玩家身上的 Health。
        Health playerHealth = player.GetComponent<Health>();

        if (playerHealth == null)
        {
            playerHealth = player.GetComponentInChildren<Health>();
        }

        if (playerHealth == null)
        {
            playerHealth = player.GetComponentInParent<Health>();
        }

        // 没有 Health 或玩家已经死亡，不处理伤害。
        if (playerHealth == null || playerHealth.IsDead)
        {
            return;
        }

        // 标记本轮冲锋已经命中过玩家。
        chargeHasHitPlayer = true;

        // 冲锋的伤害方向就是 Boss 当前冲锋方向。
        Vector3 hitDirection = chargeDirection;

        CharacterController playerCharacterController =
            player.GetComponent<CharacterController>();

        // 尽量使用玩家 CharacterController 中心作为受击位置。
        Vector3 hitPoint = playerCharacterController != null
            ? playerCharacterController.bounds.center
            : player.position + Vector3.up;

        Vector3 hitNormal = -hitDirection;

        DamageInfo damageInfo = new DamageInfo(
            chargeDamage,
            gameObject,
            hitPoint,
            hitDirection,
            hitNormal
        );

        playerHealth.TakeDamage(in damageInfo);
    }
    
    private void UpdateChargeRecover()
    {
        // 冲锋结束以后先停止移动，
        // 给玩家一个短暂的输出窗口。
        agent.isStopped = true;

        // 恢复时间还没结束，继续保持硬直。
        if (Time.time < chargePhaseEndTime)
        {
            return;
        }

        // 恢复时间结束，重新进入正常追击状态。
        EnterChaseState();
    }

    private void UpdateAttack(float distanceToPlayer)
    {
        agent.isStopped = true;

        // 攻击动画正在播放时，什么都不做。
        // 不追玩家，也不继续转向玩家。
        if (IsAttackAnimationPlaying())
        {
            attackAnimationEntered = true;
            return;
        }

        // 已经进入过攻击动画，现在又退出了，
        // 说明这一轮攻击真正播放结束。
        if (attackActive && attackAnimationEntered)
        {
            ResetAttackRuntime();
        }

        // 必须等攻击动画结束以后，
        // 才允许判断玩家是不是已经跑远。
        if (distanceToPlayer > attackDistance)
        {
            EnterChaseState();
            return;
        }

        // 攻击开始之前才允许朝向玩家。
        bool isFacingPlayer = FacePlayer();

        if (!isFacingPlayer)
        {
            return;
        }

        if (Time.time >= nextAttackTime)
        {
            attackActive = true;
            attackCommitted = false;
            attackAnimationEntered = false;

            nextAttackTime = Time.time + attackCooldown;

            animator.SetTrigger(attackHash);
        }
    }

    private void EnterChaseState()
    {
        currentState = BossState.Chase;
        agent.isStopped = false;
    }

    private void EnterAttackState()
    {
        currentState = BossState.Attack;

        agent.isStopped = true;
        agent.ResetPath();

        nextAttackTime = Time.time;
    }

    private bool IsAttackAnimationPlaying()
    {
        AnimatorStateInfo currentAnimatorState =animator.GetCurrentAnimatorStateInfo(0);

        if (currentAnimatorState.shortNameHash == attackStateHash)
        {
            return true;
        }

        // 如果给定图层上有过渡，则返回true，否则返回false
        return animator.IsInTransition(0) &&
               animator.GetNextAnimatorStateInfo(0).shortNameHash ==attackStateHash;
    }

    private void ResetAttackRuntime()
    {
        attackActive = false;
        attackAnimationEntered = false;
        attackCommitted = false;
    }

    public void AnimationEvent_AttackHit()
    {
        if (!TryCommitAttack() || player == null)
        {
            return;
        }

        float distance = GetFlatDistance(
            transform.position,
            player.position
        );

        float hitDistance =
            attackDistance + attackHitTolerance;

        if (distance > hitDistance)
        {
            Debug.Log(
                $"{name} 攻击挥空，玩家距离：{distance:F2}，" +
                $"允许命中距离：{hitDistance:F2}",
                this
            );

            return;
        }

        Health playerHealth = player.GetComponent<Health>();

        if (playerHealth == null)
        {
            playerHealth = player.GetComponentInChildren<Health>();
        }

        if (playerHealth == null)
        {
            playerHealth = player.GetComponentInParent<Health>();
        }

        if (playerHealth == null || playerHealth.IsDead)
        {
            return;
        }

        Vector3 hitDirection =
            player.position - transform.position;

        hitDirection.y = 0f;

        if (hitDirection.sqrMagnitude < 0.0001f)
        {
            hitDirection = transform.forward;
        }
        else
        {
            hitDirection.Normalize();
        }

        CharacterController playerCharacterController =player.GetComponent<CharacterController>();

        Vector3 hitPoint = playerCharacterController != null
            ? playerCharacterController.bounds.center
            : player.position + Vector3.up;

        Vector3 hitNormal = -hitDirection;

        DamageInfo damageInfo = new DamageInfo(
            attackDamage,
            gameObject,
            hitPoint,
            hitDirection,
            hitNormal
        );

        playerHealth.TakeDamage(in damageInfo);
    }
    
    private bool TryCommitAttack()
    {
        if (!attackActive || attackCommitted || currentState != BossState.Attack)
        {
            return false;
        }

        attackCommitted = true;
        return true;
    }
    
    private bool FacePlayer()
    {
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            return true;
        }

        direction.Normalize();

        Quaternion targetRotation =
            Quaternion.LookRotation(direction, Vector3.up);

        float angleToPlayer =
            Quaternion.Angle(transform.rotation, targetRotation);

        if (angleToPlayer <= attackFacingAngle)
        {
            transform.rotation = targetRotation;
            return true;
        }

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            attackRotationSpeed * Time.deltaTime
        );

        return false;
    }

    private static float GetFlatDistance(
        Vector3 first,
        Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;

        return Vector3.Distance(first, second);
    }
}
