using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(Animator))]
public class EnemyAIController : MonoBehaviour
{
    private enum EnemyState
    {
        Patrol,
        Chase,
        Attack,
        Stunned
    }

    [Header("目标")]
    [Tooltip("直接拖入玩家根对象。未赋值时会尝试寻找 Player 标签。")]
    [SerializeField] private Transform player;

    [Header("感知")]
    [SerializeField, Min(0.1f)]
    private float detectionRange = 10f;

    [Tooltip("玩家超过这个距离后，小怪放弃追击。应当大于 Detection Range。")]
    [SerializeField, Min(0.1f)]
    private float loseTargetRange = 14f;

    [Header("移动")]
    [Tooltip("小怪巡逻时的移动速度。")]
    [SerializeField, Min(0f)]
    private float patrolMoveSpeed = 1.5f;

    [Tooltip("小怪追击玩家时的移动速度。")]
    [SerializeField, Min(0f)]
    private float chaseMoveSpeed = 3.5f;
    
    
    [Header("受击减速")]
    [Tooltip("受击时移动速度倍率。0.4 表示只剩正常速度的 40%。")]
    [SerializeField, Range(0f, 1f)]
    private float hitSlowMultiplier = 0.4f;

    [Tooltip("受击减速持续时间。")]
    [SerializeField, Min(0f)]
    private float hitSlowDuration = 0.5f;
    
    [Header("巡逻")]
    [Tooltip("以小怪出生点为中心的巡逻半径。")]
    [SerializeField, Min(0.1f)]
    private float patrolRadius = 8f;

    [SerializeField]
    private Vector2 patrolWaitTime = new Vector2(1f, 3f);

    [Tooltip("随机点附近搜索 NavMesh 的范围。")]
    [SerializeField, Min(0.1f)]
    private float patrolSampleDistance = 3f;

    [Header("攻击")]
    [SerializeField, Min(0.1f)]
    private float attackRange = 1.8f;

    [SerializeField, Min(0f)]
    private float attackDamage = 15f;

    [SerializeField, Min(0.1f)]
    private float attackCooldown = 1.3f;

    [Tooltip("攻击命中时允许的额外距离，避免动画过程中目标轻微移动导致打不中。")]
    [SerializeField, Min(0f)]
    private float attackHitTolerance = 0.5f;

    [SerializeField, Min(0f)]
    private float attackRotationSpeed = 720f;
    
    [Tooltip("与玩家方向小于这个角度时，才允许开始攻击。")]
    [SerializeField, Range(0.1f, 30f)]
    private float attackFacingAngle = 5f;

    [Header("远程魔法攻击")]
    [Tooltip("追踪魔法弹预制体，预制体根节点必须带有 EnemyHomingProjectile。")]
    [SerializeField]
    private EnemyHomingProjectile homingProjectilePrefab;

    [Tooltip("魔法弹生成位置，通常放在双手之间或右手前方。")]
    [SerializeField]
    private Transform projectileSpawnPoint;

    [Tooltip("在生成点基础上向前偏移，防止魔法弹生成在小怪自身碰撞体内。")]
    [SerializeField, Min(0f)]
    private float projectileSpawnForwardOffset = 0.15f;
    
    [Header("Animator 参数")]
    [SerializeField]
    private string speedParameter = "Speed";

    [SerializeField]
    private string attackParameter = "Attack";

    [Tooltip("Animator 中攻击状态的名称，不要包含 Base Layer 前缀。")]
    [SerializeField]
    private string attackStateName = "Mutant Swiping";

    [Header("打断与硬直")]
    [Tooltip("伤害的打断力达到该值时，才可能打断攻击前摇。")]
    [SerializeField, Min(0)]
    private int interruptResistance = 1;

    [Tooltip("成功打断攻击后的硬直时间。")]
    [SerializeField, Min(0f)]
    private float stunDuration = 0.6f;

    [Tooltip("硬直结束后暂时免疫再次打断，防止连续控制。")]
    [SerializeField, Min(0f)]
    private float interruptImmunityDuration = 0.8f;

    [Tooltip("可选。Animator 中用于播放受击硬直动画的 Trigger。")]
    [SerializeField]
    private string hitTriggerParameter = "Hit";

    private NavMeshAgent agent;
    private Animator animator;
    private Health health;

    private EnemyState currentState;
    private Vector3 patrolCenter;

    private float waitEndTime;
    private float nextAttackTime;
    private bool isWaiting;
    private float hitSlowEndTime;
    private float stunEndTime;
    private float interruptImmunityEndTime;
    // 表示当前是否存在一轮有效攻击。
    private bool attackActive;
    // 表示攻击是否已经到达伤害生效帧。
    private bool attackCommitted;
    // 用于确认 Animator 是否真正进入过攻击状态。
    private bool attackAnimationEntered;
    private bool hasHitTrigger;

    private int speedHash;
    private int attackHash;
    private int attackStateHash;
    private int hitTriggerHash;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        animator = GetComponent<Animator>();
        health = GetComponent<Health>();

        speedHash = Animator.StringToHash(speedParameter);
        attackHash = Animator.StringToHash(attackParameter);
        attackStateHash = Animator.StringToHash(attackStateName);
        hitTriggerHash = Animator.StringToHash(hitTriggerParameter);
        hasHitTrigger = HasAnimatorTrigger(hitTriggerParameter);
    }
    
    public void ResetForReuse()
    {
        enabled = true;
        patrolCenter = transform.position;
        isWaiting = false;
        waitEndTime = 0f;
        nextAttackTime = Time.time;
        hitSlowEndTime = 0f;
        stunEndTime = 0f;
        interruptImmunityEndTime = 0f;
        ResetAttackRuntime();

        animator.ResetTrigger(attackHash);

        if (hasHitTrigger)
        {
            animator.ResetTrigger(hitTriggerHash);
        }

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        if (agent == null || !agent.enabled)
        {
            return;
        }

        agent.stoppingDistance = attackRange * 0.85f;

        if (!agent.isOnNavMesh)
        {
            animator.SetFloat(speedHash, 0f);
            return;
        }

        agent.isStopped = false;
        agent.ResetPath();
        EnterPatrolState();
    }


    private void Start()
    {
        patrolCenter = transform.position;

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");

            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        // 让小怪在略小于攻击距离的位置停止。
        agent.stoppingDistance = attackRange * 0.85f;

        EnterPatrolState();

        if (homingProjectilePrefab != null)
        {
            PoolManager poolManager = GameEntry.Pool;

            if (poolManager != null)
            {
                poolManager.WarmEnemyProjectilePool(homingProjectilePrefab);
            }
        }
    }

    private void Update()
    {
        if (health != null && health.IsDead)
        {
            StopAfterDeath();
            return;
        }

        // 小怪必须处于已经烘焙的 NavMesh 上。
        if (!agent.isOnNavMesh)
        {
            animator.SetFloat(speedHash, 0f);
            return;
        }

        float distanceToPlayer = player == null
            ? float.PositiveInfinity
            : GetFlatDistance(transform.position, player.position);

        switch (currentState)
        {
            case EnemyState.Patrol:
                UpdatePatrol(distanceToPlayer);
                break;

            case EnemyState.Chase:
                UpdateChase(distanceToPlayer);
                break;

            case EnemyState.Attack:
                UpdateAttack(distanceToPlayer);
                break;

            case EnemyState.Stunned:
                UpdateStunned();
                break;
        }

        // 根据受击状态更新真正移动速度
        UpdateMoveSpeed();
        
        UpdateMoveAnimation();
    }

    private void StopAfterDeath()
    {
        ResetAttackRuntime();
        animator.ResetTrigger(attackHash);
        animator.SetFloat(speedHash, 0f);

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        enabled = false;
    }
    
    private void UpdatePatrol(float distanceToPlayer)
    {
        if (distanceToPlayer <= detectionRange)
        {
            EnterChaseState();
            return;
        }

        if (agent.pathPending)
        {
            return;
        }

        bool reachedDestination =
            !agent.hasPath ||
            agent.remainingDistance <= agent.stoppingDistance + 0.15f;

        if (!reachedDestination)
        {
            return;
        }

        if (!isWaiting)
        {
            isWaiting = true;
            agent.isStopped = true;

            waitEndTime = Time.time +
                          Random.Range(patrolWaitTime.x, patrolWaitTime.y);

            return;
        }

        if (Time.time >= waitEndTime)
        {
            isWaiting = false;
            agent.isStopped = false;

            SetNewPatrolDestination();
        }
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Damaged += OnDamaged;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
        }
    }

    private void OnDamaged(DamageInfo damageInfo)
    {
        // 致死伤害不用再处理减速
        if (health == null || health.CurrentHealth <= 0f)
        {
            return;
        }

        // 每次受击都会重新刷新减速时间
        hitSlowEndTime = Time.time + hitSlowDuration;

        // 检查这次伤害能否打断当前攻击
        TryInterruptAttack(damageInfo);
    }
    
    private void UpdateMoveSpeed()
    {
        float multiplier =Time.time < hitSlowEndTime? hitSlowMultiplier: 1f;

        switch (currentState)
        {
            case EnemyState.Patrol:
                agent.speed = patrolMoveSpeed * multiplier;
                break;

            case EnemyState.Chase:
                agent.speed = chaseMoveSpeed * multiplier;
                break;
        }
    }
    
    private void UpdateChase(float distanceToPlayer)
    {
        if (player == null || distanceToPlayer >= loseTargetRange)
        {
            EnterPatrolState();
            return;
        }

        if (distanceToPlayer <= attackRange)
        {
            EnterAttackState();
            return;
        }

        agent.isStopped = false;
        agent.SetDestination(player.position);
    }

    private void UpdateAttack(float distanceToPlayer)
    {
        if (player == null)
        {
            EnterPatrolState();
            return;
        }

        // 进入攻击状态后停止移动。
        agent.isStopped = true;

        /*
         * 攻击动画正在播放时直接返回。
         *
         * 这里不会调用 FacePlayer()，
         * 因此攻击过程中不会继续转向玩家。
         */
        if (IsAttackAnimationPlaying())
        {
            attackAnimationEntered = true;
            return;
        }

        // 动画已经进入过攻击状态，之后又离开，说明本轮攻击自然结束。
        if (attackActive && attackAnimationEntered)
        {
            ResetAttackRuntime();
        }

        /*
         * 只有当前攻击动画结束后，
         * 才判断玩家是否已经离开攻击范围。
         */
        if (distanceToPlayer > attackRange + attackHitTolerance)
        {
            EnterChaseState();
            return;
        }

        /*
         * 攻击动画尚未开始时，先朝向玩家。
         * 返回 true 表示已经基本对准。
         */
        bool isFacingPlayer = FacePlayer();

        if (!isFacingPlayer)
        {
            return;
        }

        /*
         * 已经对准玩家，并且攻击冷却结束，
         * 才正式触发攻击动画。
         */
        if (Time.time >= nextAttackTime)
        {
            // 创建一轮新的有效攻击。
            // 当前还没有到达生效帧。
            // Animator 还没有确认进入攻击状态。
            attackActive = true;
            attackCommitted = false;
            attackAnimationEntered = false;
            nextAttackTime = Time.time + attackCooldown;
            animator.SetTrigger(attackHash);
        }
    }

    private void UpdateStunned()
    {
        agent.isStopped = true;
        animator.SetFloat(speedHash, 0f);

        if (Time.time < stunEndTime)
        {
            return;
        }

        if (player == null)
        {
            EnterPatrolState();
            return;
        }

        EnterChaseState();
    }

    private bool TryInterruptAttack(DamageInfo damageInfo)
    {
        // 有打断力
        // + 打断力达到抗性
        // + 小怪正在攻击
        // + 攻击仍有效
        // + 还处于前摇
        // + 不在保护期
        // = 成功打断
        if (damageInfo.InterruptPower <= 0 ||
            damageInfo.InterruptPower < interruptResistance ||
            currentState != EnemyState.Attack ||
            !attackActive ||
            attackCommitted ||
            Time.time < interruptImmunityEndTime)
        {
            return false;
        }

        InterruptCurrentAttack(damageInfo);
        return true;
    }

    private void InterruptCurrentAttack(DamageInfo damageInfo)
    {
        ResetAttackRuntime();
        animator.ResetTrigger(attackHash);

        if (hasHitTrigger)
        {
            animator.SetTrigger(hitTriggerHash);
        }

        currentState = EnemyState.Stunned;
        stunEndTime = Time.time + stunDuration;
        interruptImmunityEndTime =stunEndTime + interruptImmunityDuration;
            
        nextAttackTime = Mathf.Max(nextAttackTime, stunEndTime);

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        Debug.Log(
            $"{name} 的攻击被打断：" +
            $"打断力={damageInfo.InterruptPower}，" +
            $"抗性={interruptResistance}，" +
            $"硬直={stunDuration:F2} 秒。",
            this
        );
    }

    private void ResetAttackRuntime()
    {
        attackActive = false;
        attackCommitted = false;
        attackAnimationEntered = false;
    }

    private bool TryCommitAttack()
    {
        if (!attackActive ||
            attackCommitted ||
            currentState != EnemyState.Attack ||
            health == null ||
            health.IsDead)
        {
            return false;
        }

        attackCommitted = true;
        return true;
    }

    private bool HasAnimatorTrigger(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger &&
                parameter.name == parameterName)
            {
                return true;
            }
        }

        Debug.LogWarning(
            $"{name} 的 Animator 没有 Trigger 参数 {parameterName}。" +
            "打断逻辑仍会生效，但不会播放受击动画。",
            this
        );
        return false;
    }

    private bool IsAttackAnimationPlaying()
    {
        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);

        if (currentState.shortNameHash == attackStateHash)
        {
            return true;
        }

        return animator.IsInTransition(0) &&
               animator.GetNextAnimatorStateInfo(0).shortNameHash == attackStateHash;
    }

    private void EnterPatrolState()
    {
        currentState = EnemyState.Patrol;

        isWaiting = false;
        agent.isStopped = false;

        // 设置巡逻速度。
        agent.speed = patrolMoveSpeed;

        SetNewPatrolDestination();
    }

    private void EnterChaseState()
    {
        currentState = EnemyState.Chase;

        isWaiting = false;
        agent.isStopped = false;

        // 设置追击速度。
        agent.speed = chaseMoveSpeed;

        if (player != null)
        {
            agent.SetDestination(player.position);
        }
    }

    private void EnterAttackState()
    {
        currentState = EnemyState.Attack;

        agent.isStopped = true;
        agent.ResetPath();

        // 进入攻击状态后立即允许攻击。
        nextAttackTime = Time.time;
    }

    private void SetNewPatrolDestination()
    {
        const int maxAttempts = 20;

        for (int i = 0; i < maxAttempts; i++)
        {
            Vector3 randomOffset = Random.insideUnitSphere * patrolRadius;
            randomOffset.y = 0f;

            Vector3 randomPosition = patrolCenter + randomOffset;

            if (NavMesh.SamplePosition(
                    randomPosition,
                    out NavMeshHit hit,
                    patrolSampleDistance,
                    NavMesh.AllAreas))
            {
                agent.SetDestination(hit.position);
                return;
            }
        }

        Debug.LogWarning(
            $"{name} 没有找到可用的随机巡逻点，请检查 NavMesh 和巡逻半径。",
            this);
    }

    /// <summary>
    /// 在攻击开始前转向玩家。
    /// 返回 true 表示已经基本对准玩家，可以开始攻击。
    /// </summary>
    private bool FacePlayer()
    {
        if (player == null)
        {
            return false;
        }

        Vector3 direction =
            player.position - transform.position;

        // 只在水平面上转向。
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            return true;
        }

        direction.Normalize();

        Quaternion targetRotation =
            Quaternion.LookRotation(
                direction,
                Vector3.up
            );

        // 计算当前朝向与玩家方向之间的角度。
        float angleToPlayer =
            Quaternion.Angle(
                transform.rotation,
                targetRotation
            );

        /*
         * 已经进入允许误差范围：
         * 直接精确对齐，避免剩余的小角度误差。
         */
        if (angleToPlayer <= attackFacingAngle)
        {
            transform.rotation = targetRotation;
            return true;
        }

        // 尚未对准时继续平滑旋转。
        transform.rotation =
            Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                attackRotationSpeed * Time.deltaTime
            );

        return false;
    }

    private void UpdateMoveAnimation()
    {
        float normalizedSpeed = 0f;

        if (!agent.isStopped && agent.speed > 0.01f)
        {
            normalizedSpeed = agent.velocity.magnitude / agent.speed;
        }

        animator.SetFloat(
            speedHash,
            normalizedSpeed,
            0.1f,
            Time.deltaTime);
    }

    /// <summary>
    /// 在攻击动画真正命中的那一帧，通过 Animation Event 调用。
    /// </summary>
    public void AnimationEvent_AttackHit()
    {
        if (!TryCommitAttack())
        {
            return;
        }

        // 小怪已经死亡，或者玩家不存在，不处理伤害。
        if ((health != null && health.IsDead) || player == null)
        {
            return;
        }

        /*
         * 使用小怪根节点和玩家根节点的 XZ 平面距离进行命中判定。
         * 忽略 Y 轴高度差。
         */
        float distance = GetFlatDistance(
            transform.position,
            player.position
        );

        float hitDistance =
            attackRange + attackHitTolerance;

        // 动画播放到命中帧时，玩家已经离开攻击范围，本次攻击挥空。
        if (distance > hitDistance)
        {
            Debug.Log(
                $"{name} 攻击挥空，玩家距离：{distance:F2}，" +
                $"允许命中距离：{hitDistance:F2}",
                this
            );

            return;
        }

        // 在玩家根对象、子对象或父对象上寻找 Health。
        Health playerHealth = player.GetComponent<Health>();

        if (playerHealth == null)
        {
            playerHealth = player.GetComponentInChildren<Health>();
        }

        if (playerHealth == null)
        {
            playerHealth = player.GetComponentInParent<Health>();
        }

        if (playerHealth == null)
        {
            Debug.LogWarning(
                $"没有在玩家 {player.name} 上找到 Health 组件。",
                player
            );

            return;
        }

        if (playerHealth.IsDead)
        {
            return;
        }

        /*
         * 伤害方向：
         * 从小怪指向玩家。
         */
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

        /*
         * 伤害位置：
         * 优先使用玩家 CharacterController 的中心，
         * 找不到时使用玩家根节点上方 1 米。
         */
        CharacterController playerCharacterController =
            player.GetComponent<CharacterController>();

        Vector3 hitPoint = playerCharacterController != null
            ? playerCharacterController.bounds.center
            : player.position + Vector3.up;

        // 命中表面法线朝向攻击者。
        // hitDirection 是小怪指向玩家，所以取反后是玩家指向小怪。
        Vector3 hitNormal = -hitDirection;

        DamageInfo damageInfo = new DamageInfo(
            attackDamage,  // 伤害数值
            gameObject,    // 伤害来源：当前小怪
            hitPoint,      // 命中位置
            hitDirection,  // 伤害方向：小怪指向玩家
            hitNormal      // 命中法线：玩家指向小怪
        );
        
        playerHealth.TakeDamage(in damageInfo);

        Debug.Log(
            $"{name} 命中玩家，造成 {attackDamage} 点伤害，" +
            $"玩家剩余生命：{playerHealth.CurrentHealth}",
            this
        );
    }

    /// <summary>
    /// 远程小怪的施法动画播放到释放帧时，由 Animation Event 调用。
    /// 近战动画继续调用 AnimationEvent_AttackHit，不要混用。
    /// </summary>
    public void AnimationEvent_FireHomingProjectile()
    {
        if (!TryCommitAttack())
        {
            return;
        }

        // 小怪死亡或者玩家不存在，不生成魔法弹。
        if ((health != null && health.IsDead) || player == null)
        {
            return;
        }

        if (homingProjectilePrefab == null)
        {
            Debug.LogWarning(
                $"{name} 没有配置 Homing Projectile Prefab。",
                this
            );

            return;
        }

        if (projectileSpawnPoint == null)
        {
            Debug.LogWarning(
                $"{name} 没有配置 Projectile Spawn Point。",
                this
            );

            return;
        }

        /*
         * 先获取玩家身体中心。
         * 这里只决定魔法弹出生时的初始方向。
         * 后续持续转向由 EnemyHomingProjectile 自己处理。
         */
        CharacterController playerCharacterController =
            player.GetComponent<CharacterController>();

        if (playerCharacterController == null)
        {
            playerCharacterController =
                player.GetComponentInChildren<CharacterController>();
        }

        Vector3 targetPosition =
            playerCharacterController != null
                ? playerCharacterController.bounds.center
                : player.position + Vector3.up;

        Vector3 initialDirection =
            targetPosition - projectileSpawnPoint.position;

        if (initialDirection.sqrMagnitude < 0.0001f)
        {
            initialDirection = transform.forward;
        }

        initialDirection.Normalize();

        /*
         * 沿真正的发射方向向前偏移。
         * 即使小怪当前没有完全转向玩家，生成位置也不会偏到错误方向。
         */
        Vector3 spawnPosition =
            projectileSpawnPoint.position +
            initialDirection * projectileSpawnForwardOffset;

        Quaternion spawnRotation =
            Quaternion.LookRotation(
                initialDirection,
                Vector3.up
            );

        PoolManager poolManager = GameEntry.Pool;

        if (poolManager == null)
        {
            return;
        }

        EnemyHomingProjectile projectile =
            poolManager.GetEnemyProjectile(
                homingProjectilePrefab,
                spawnPosition,
                spawnRotation
            );

        if (projectile == null)
        {
            return;
        }

        /*
         * 把玩家 Transform、伤害和发射者传给追踪弹。
         */
        projectile.Initialize(
            player,
            attackDamage,
            gameObject
        );
    }

    /// <summary>
    /// 可选的攻击动画末帧事件。即使未配置，UpdateAttack 也会兜底结束攻击。
    /// </summary>
    public void AnimationEvent_AttackFinished()
    {
        ResetAttackRuntime();
    }
    
    private static float GetFlatDistance(Vector3 first, Vector3 second)
    {
        first.y = 0f;
        second.y = 0f;

        return Vector3.Distance(first, second);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = Application.isPlaying
            ? patrolCenter
            : transform.position;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, patrolRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectionRange);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
