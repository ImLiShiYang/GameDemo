using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering.Universal;

public class BossController : MonoBehaviour
{
    private enum BossState
    {
        Chase,
        Attack,
        //冲锋
        Charge,
        // 砸地
        Slam,
        Stunned,
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

    
    private enum SlamPhase
    {
        Windup,
        Impact,
        Recover
    }

    private enum BossCombatAction
    {
        None,
        Melee,
        Charge,
        Slam
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

    [Header("Interrupt")]
    [Tooltip("Interrupt power required to cancel an uncommitted Boss attack.")]
    [SerializeField, Min(1)] private int interruptResistance = 2;

    [Tooltip("How long the Boss remains stunned after an interrupt.")]
    [SerializeField, Min(0f)] private float stunDuration = 0.8f;

    [Tooltip("Temporary interrupt immunity after recovering from a stun.")]
    [SerializeField, Min(0f)] private float interruptImmunityDuration = 0.35f;
    
    
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
    
    [SerializeField] private DecalProjector chargeWarningDecal;
    [SerializeField] private float chargeWarningWidth = 2f;
    [SerializeField] private float chargeWarningHeight = 1f;
    [SerializeField] private float chargeWarningProjectionDepth = 2f;
    
    [Header("Ground Slam")]

    [SerializeField] private float slamDistance = 5f;

    [SerializeField] private float slamCooldown = 8f;

    [SerializeField] private float slamDamage = 50f;

    [SerializeField] private float slamRadius = 4f;

    [SerializeField] private float slamWindupDuration = 1f;

    [SerializeField] private DecalProjector slamWarningDecal;
    [SerializeField] private float slamJumpSpeed = 8f;

    [SerializeField] private float slamStopDistance = 0.5f;

    [Tooltip("无法从动画事件读取间隔时使用的备用水平移动时长。")]
    [SerializeField, Min(0.01f)] private float slamJumpTravelDuration = 0.9f;

    [Tooltip("在玩家附近搜索可落脚 NavMesh 位置的半径。")]
    [SerializeField, Min(0.1f)] private float slamTargetSampleRadius = 2f;
    
    [SerializeField] private float slamWarningHeight = 0.05f;

    [SerializeField, Min(0.1f)] private float slamWarningProjectionDepth = 2f;

    [Header("Combat Decision")]
    [Tooltip("处于 Chase 时重新评估战斗动作的时间间隔。")]
    [SerializeField, Min(0.01f)] private float combatDecisionInterval = 0.15f;

    [Tooltip("Charge 或 Slam 完整结束后，再次允许特殊技能的等待时间。")]
    [SerializeField, Min(0f)] private float specialSkillGap = 0.8f;

    [Tooltip("上一次特殊技能再次参与选择时的分数倍率。")]
    [SerializeField, Range(0f, 1f)] private float repeatSkillPenalty = 0.35f;

    [Tooltip("只在达到最高分这一比例的候选动作中进行加权随机。")]
    [SerializeField, Range(0f, 1f)] private float topScoreSelectionRatio = 0.85f;

    [SerializeField, Min(0f)] private float slamMinDecisionDistance = 2f;
    [SerializeField, Min(0f)] private float slamPreferredDistance = 5.5f;
    [SerializeField, Min(0f)] private float chargePreferredDistance = 10.5f;
    [SerializeField, Range(-1f, 1f)] private float chargeMinFacingDot = 0.25f;

    [SerializeField, Min(0f)] private float meleeUtilityWeight = 0.9f;
    [SerializeField, Min(0f)] private float chargeUtilityWeight = 1f;
    [SerializeField, Min(0f)] private float slamUtilityWeight = 1.05f;

    [Header("Skill Obstacle Detection")]
    [Tooltip("技能路径检测使用的障碍层。默认检测 Default 和 Environment。")]
    [SerializeField] private LayerMask skillObstacleMask =
        (1 << 0) | (1 << 8);

    [Tooltip("Charge 障碍检测半径相对 NavMeshAgent 半径的倍率。")]
    [SerializeField, Range(0.1f, 1f)]
    private float chargeObstacleRadiusScale = 0.9f;

    [Tooltip("Charge 停在障碍物前保留的距离。")]
    [SerializeField, Min(0f)] private float skillObstacleSkin = 0.1f;

    [Tooltip("检测玩家附近 NavMesh 点时使用的半径。")]
    [SerializeField, Min(0.1f)] private float skillPathSampleRadius = 1.5f;

    [Tooltip("Slam 只在这个高度检测高墙，低矮障碍允许跳过。")]
    [SerializeField, Min(0f)] private float slamWallCheckHeight = 1.2f;

    [SerializeField, Min(0.01f)] private float slamWallCheckRadius = 0.25f;
    

    [Header("Animator")] 
    [SerializeField] private BossAnimationController bossAnimationController;
    [SerializeField] private string attackParameter = "Attack";
    [SerializeField] private string attackStateName = "Attack1";
    [SerializeField] private string deadParameter = "Dead";
    [SerializeField] private string deadStateName = "Dead";
    [SerializeField, Min(0f)] private float deathTransitionDuration = 0.1f;

    [SerializeField] private string slamStateName = "Slam";
    
    // Boss 当前所处的大状态。
    // 例如：Chase、Attack、Charge、Slam、Dead。
    private BossState currentState;

    private BossCombatAction lastSpecialAction;
    private float nextCombatDecisionTime;
    private float nextSpecialSkillTime;
    private readonly RaycastHit[] skillObstacleHits = new RaycastHit[16];

    // 下一次允许进行普通攻击的时间。
    // 通过 Time.time >= nextAttackTime 判断普通攻击冷却是否结束。
    private float nextAttackTime;
    private float stunEndTime;
    private float interruptImmunityEndTime;

    // 当前是否已经开启了一轮普通攻击。
    // 从触发攻击动画开始，到攻击动画播放结束期间为 true。
    private bool attackActive;

    // 当前这一轮普通攻击是否真正进入过攻击动画状态。
    // 用来判断攻击动画是否已经完整播放并退出。
    private bool attackAnimationEntered;

    // 当前这一轮普通攻击是否已经执行过伤害判定。
    // 防止同一次攻击动画的 Animation Event 重复造成伤害。
    private bool attackCommitted;


    // 普通攻击 Animator 参数的 Hash。
    // 用于通过 Animator.SetTrigger() 触发普通攻击动画。
    private int attackHash;

    // 普通攻击 Animator State 名称对应的 Hash。
    // 用于判断当前 Animator 是否正在播放普通攻击动画。
    private int attackStateHash;

    // 死亡 Animator 参数对应的 Hash。
    private int deadHash;

    // Slam Animator State 名称对应的 Hash。
    // 用于判断当前 Animator 是否正在播放 Slam 动画。
    private int slamStateHash;


    // 本次 Slam 开始时锁定的最终落点。
    // Slam 开始后即使玩家继续移动，这个位置也不会再改变。
    private Vector3 slamTargetPosition;

    // Boss 从当前位置指向 Slam 锁定落点的水平方向。
    // Slam 开始时计算并锁定。
    private Vector3 slamDirection;

    // 当前 Slam 是否正在执行水平位移。
    // AnimationEvent_SlamJump 触发后变为 true，落地后变为 false。
    private bool slamMoving;

    // 当前这一轮 Slam 是否真正进入过 Slam 动画状态。
    // 用来判断 Slam 动画是否已经播放并最终退出。
    private bool slamAnimationEntered;

    // Slam 开始水平移动时 Boss 的世界坐标。
    // 用来和 slamTargetPosition 做插值，计算每一帧的位置。
    private Vector3 slamMoveStartPosition;

    // 当前 Slam 水平移动已经经过的时间。
    private float slamMoveElapsed;

    // 本次 Slam 从起跳到落地需要进行水平移动的总时间。
    // 优先根据 SlamJump 和 SlamHit 两个 Animation Event 的时间间隔计算。
    private float slamMoveDuration;


    // 进入 Slam 之前 Animator.applyRootMotion 原本的值。
    // Slam 结束后通过这个值恢复原来的 Root Motion 设置。
    private bool slamPreviousApplyRootMotion;

    // 当前是否已经临时修改过 Animator.applyRootMotion。
    // 防止重复关闭或重复恢复 Root Motion。
    private bool slamRootMotionOverridden;

    
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
    
    
    // 当前 Slam 所处的内部阶段。
    // Windup：起跳前蓄力。
    // Impact：起跳后正在向锁定点移动。
    // Recover：已经落地，等待 Slam 动画完整结束。
    private SlamPhase slamPhase;

    // 原本用于记录 Slam 蓄力结束时间。
    // 当前代码已经没有使用这个变量，Slam 起跳时机现在由 AnimationEvent_SlamJump 控制。
    private float slamEndTime;

    // 原本用于记录当前 Slam 是否已经造成过伤害。
    // 当前代码已经没有使用这个变量，伤害现在直接由 AnimationEvent_SlamHit 执行。
    private bool slamHit;

    // 下一次允许发动 Slam 的时间。
    // 通过 Time.time >= nextSlamTime 判断 Slam 冷却是否结束。
    private float nextSlamTime;

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
        slamStateHash = Animator.StringToHash(slamStateName);
        
        HideSlamWarning();
        HideChargeWarning();
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Damaged += OnDamaged;
            health.Died += HandleDied;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
            health.Died -= HandleDied;
        }

        RestoreSlamRootMotion();
    }

    private void OnDamaged(DamageInfo damageInfo)
    {
        if (health == null || health.CurrentHealth <= 0f)
        {
            return;
        }

        TryInterruptAttack(damageInfo);
    }

    private bool TryInterruptAttack(DamageInfo damageInfo)
    {
        if (damageInfo.InterruptPower <= 0 ||
            damageInfo.InterruptPower < interruptResistance ||
            Time.time < interruptImmunityEndTime ||
            !IsAttackInterruptible())
        {
            return false;
        }

        InterruptCurrentAttack(damageInfo);
        return true;
    }

    private bool IsAttackInterruptible()
    {
        return currentState switch
        {
            BossState.Attack => attackActive && !attackCommitted,
            BossState.Charge => chargePhase == ChargePhase.Windup,
            BossState.Slam => slamPhase == SlamPhase.Windup,
            _ => false
        };
    }

    private void InterruptCurrentAttack(DamageInfo damageInfo)
    {
        ResetAttackRuntime();
        animator.ResetTrigger(attackHash);
        animator.ResetTrigger(slamStateHash);
        HideChargeWarning();
        HideSlamWarning();
        slamMoving = false;
        RestoreSlamRootMotion();

        if (bossAnimationController != null)
        {
            bossAnimationController.PlayHit();
        }

        currentState = BossState.Stunned;
        stunEndTime = Time.time + stunDuration;
        interruptImmunityEndTime = stunEndTime + interruptImmunityDuration;
        nextAttackTime = Mathf.Max(nextAttackTime, stunEndTime);

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        Debug.Log(
            $"{name} attack interrupted: power={damageInfo.InterruptPower}, " +
            $"resistance={interruptResistance}, stun={stunDuration:F2}s.",
            this
        );
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
        
        HideChargeWarning();
        HideSlamWarning();
        slamMoving = false;
        RestoreSlamRootMotion();
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

        float distanceToPlayer = GetFlatDistance(transform.position,player.position);

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
            
            case BossState.Slam:
                UpdateSlam();
                break;

            case BossState.Stunned:
                UpdateStunned();
                break;
        }
    }

    private void UpdateStunned()
    {
        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
        }

        if (Time.time >= stunEndTime)
        {
            EnterChaseState();
        }
    }
    
    private void UpdateSlam()
    {
        agent.isStopped = true;

        if(slamMoving)
        {
            UpdateSlamMove();
        }

        bool isSlamAnimationPlaying = IsSlamAnimationPlaying();

        if(isSlamAnimationPlaying)
        {
            slamAnimationEntered = true;
        }

        // SlamHit 只表示落地命中，不表示整段 Slam 动画已经结束。
        // 必须等 Animator 真正退出 Slam，才能恢复 NavMeshAgent 追击，
        // 否则落地后的收势动画会被追击移动拖着滑行。
        if(slamPhase == SlamPhase.Recover && slamAnimationEntered && !isSlamAnimationPlaying)
        {
            RestoreSlamRootMotion();
            ScheduleSpecialSkillGap();
            EnterChaseState();
        }
    }

    /// <summary>
    /// 更新 Slam 起跳后的水平移动。
    /// 根据已经经过的时间计算当前移动进度，
    /// 再让 Boss 从起跳位置平滑移动到之前锁定的 Slam 落点。
    /// </summary>
    private void UpdateSlamMove()
    {
        // 累加本次 Slam 水平移动已经经过的时间。
        slamMoveElapsed += Time.deltaTime;

        // 计算当前移动进度。
        //
        // slamMoveElapsed：
        // 已经移动了多长时间。
        //
        // slamMoveDuration：
        // 从 SlamJump 到 SlamHit 整段移动应该持续多久。
        //
        // 例如：
        // 已经过了 0.45 秒，
        // 总移动时间是 0.9 秒，
        // 那么 progress = 0.5。
        //
        // Clamp01 会把结果限制在 0 ~ 1 之间，
        // 防止因为帧率误差让 progress 超过 1。
        float progress = Mathf.Clamp01(slamMoveElapsed / Mathf.Max(0.01f, slamMoveDuration));

        // 对线性的 progress 做一次 SmoothStep。
        //
        // 原始 progress 是匀速变化：
        // 0 -> 0.1 -> 0.2 -> 0.3 -> ... -> 1
        //
        // SmoothStep 会让：
        // 起跳时移动慢一点，
        // 中间移动快一点，
        // 落地前再慢下来。
        //
        // 这样 Boss 的水平位移看起来会更自然，
        // 不会从起跳第一帧就突然以恒定速度冲出去。
        float curvedProgress = Mathf.SmoothStep(0f, 1f, progress);

        // 根据当前平滑后的进度，
        // 计算 Boss 这一帧理论上应该到达的世界坐标。
        //
        // slamMoveStartPosition：
        // SlamJump 触发时 Boss 的起跳位置。
        //
        // slamTargetPosition：
        // Slam 开始时已经锁定好的最终落点。
        //
        // 当 curvedProgress = 0 时：
        // desiredPosition = 起跳位置。
        //
        // 当 curvedProgress = 1 时：
        // desiredPosition = Slam 最终落点。
        Vector3 desiredPosition = Vector3.Lerp(slamMoveStartPosition,slamTargetPosition,curvedProgress);
        

        // 计算 Boss 当前实际位置到这一帧目标位置之间的位移差。
        //
        // 注意这里不是直接拿 slamTargetPosition - transform.position，
        // 而是每帧先算出一个中间目标位置 desiredPosition，
        // 所以 Boss 会按照整个 Slam 时间逐渐接近最终落点。
        Vector3 move = desiredPosition - transform.position;

        // Slam 的代码位移只负责 X、Z 水平移动。
        // Y 轴上的起跳、腾空、落地视觉效果由 Slam 动画负责。
        move.y = 0f;

        // 让 NavMeshAgent 按这一帧计算出的位移进行移动。
        agent.Move(move);

        // 当 progress 已经达到 1，
        // 说明从起跳到落地所需要的移动时间已经全部经过。
        if(progress >= 1f)
        {
            // 停止继续执行 Slam 水平移动。
            // 最终落地帧还会由 AnimationEvent_SlamHit
            // 再把 Boss 精确修正到 slamTargetPosition。
            slamMoving = false;
        }
    }
    
    public void AnimationEvent_SlamJump()
    {
        if(currentState != BossState.Slam)
        {
            return;
        }

        slamAnimationEntered = true;

        slamMoveStartPosition = transform.position;
        slamMoveElapsed = 0f;
        slamMoveDuration = ResolveSlamMoveDuration();

        slamPhase = SlamPhase.Impact;
        slamMoving = true;
    }
    
    /// <summary>
    /// 计算本次 Slam 从起跳到落地之间的水平移动时长。
    /// 优先读取当前 Slam 动画中 SlamJump 和 SlamHit 两个 Animation Event 的时间间隔，
    /// 并根据 Animator 当前播放速度换算成实际游戏时间。
    /// 如果无法正确读取动画事件，则使用配置的备用移动时间和移动速度进行计算。
    /// </summary>
    /// <returns>本次 Slam 水平移动需要持续的时间。</returns>
    private float ResolveSlamMoveDuration()
    {
        // 获取 Animator 第 0 层当前正在播放的所有动画 Clip 信息。
        // 正常情况下 Slam 状态通常只有一个主要动画 Clip，
        // 但如果 Animator 正处于混合或过渡状态，也可能返回多个 Clip。
        AnimatorClipInfo[] clipInfos = animator.GetCurrentAnimatorClipInfo(0);

        // 获取 Animator 第 0 层当前状态的信息。
        // 后面需要用这里面的 speed 和 speedMultiplier 计算动画实际播放速度。
        AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);

        // 遍历当前正在参与播放的所有动画 Clip。
        foreach(AnimatorClipInfo clipInfo in clipInfos)
        {
            // SlamJump 动画事件在当前 Clip 中的时间。
            // 初始为 -1，表示暂时还没有找到这个事件。
            float jumpEventTime = -1f;

            // SlamHit 动画事件在当前 Clip 中的时间。
            // 初始为 -1，表示暂时还没有找到这个事件。
            float hitEventTime = -1f;

            // 遍历当前动画 Clip 中配置的所有 Animation Event。
            foreach(AnimationEvent animationEvent in clipInfo.clip.events)
            {
                // 找到 SlamJump 事件时，记录它在动画中的时间。
                if(animationEvent.functionName == nameof(AnimationEvent_SlamJump))
                {
                    jumpEventTime = animationEvent.time;
                }
                // 找到 SlamHit 事件时，记录它在动画中的时间。
                else if(animationEvent.functionName == nameof(AnimationEvent_SlamHit))
                {
                    hitEventTime = animationEvent.time;
                }
            }

            // 必须同时满足：
            // 1. 已经找到 SlamJump 事件。
            // 2. SlamHit 事件出现在 SlamJump 事件之后。
            // 才能直接使用动画事件之间的时间差作为移动时长。
            if(jumpEventTime >= 0f && hitEventTime > jumpEventTime)
            {
                // 计算 Animator 当前真正的播放速度。
                //
                // stateInfo.speed：
                // Animator State 本身配置的 Speed。
                //
                // stateInfo.speedMultiplier：
                // Animator State 的速度倍率。
                //
                // animator.speed：
                // 整个 Animator 的全局播放速度。
                //
                // 三者相乘得到当前动画的实际播放速度。
                float playbackSpeed = Mathf.Abs(stateInfo.speed *stateInfo.speedMultiplier *animator.speed);
                

                // 动画中 SlamJump 到 SlamHit 的原始时间差，
                // 除以当前动画实际播放速度，
                // 得到游戏中真正经过的时间。
                //
                // 例如：
                // Jump 在 0.4 秒，Hit 在 1.3 秒，
                // 原始间隔 = 0.9 秒。
                //
                // 如果动画播放速度是 2 倍，
                // 实际移动时间就是 0.9 / 2 = 0.45 秒。
                return (hitEventTime - jumpEventTime) /
                       Mathf.Max(0.01f, playbackSpeed);
            }
        }

        // 如果没有正确读取到 SlamJump / SlamHit 动画事件，
        // 就进入备用计算逻辑。

        // 计算 Boss 当前所在位置到 Slam 锁定落点之间的距离。
        Vector3 fallbackDistance = slamTargetPosition - transform.position;

        // Slam 的代码移动只负责水平面的 X、Z，
        // 所以忽略 Y 轴高度差。
        fallbackDistance.y = 0f;

        // 返回备用移动时长。
        //
        // fallbackDistance.magnitude / slamJumpSpeed
        // 表示按照配置的 slamJumpSpeed 移动到目标点理论上需要多少秒。
        //
        // 再和 slamJumpTravelDuration 比较，
        // 取两者中较大的一个，避免移动时间过短。
        //
        // Mathf.Max(0.01f, slamJumpSpeed)
        // 是为了防止 slamJumpSpeed 为 0 时出现除以 0。
        return Mathf.Max(
            slamJumpTravelDuration,
            fallbackDistance.magnitude / Mathf.Max(0.01f, slamJumpSpeed)
        );
    }

    private bool IsSlamAnimationPlaying()
    {
        AnimatorStateInfo currentState =animator.GetCurrentAnimatorStateInfo(0);

        if(currentState.shortNameHash == slamStateHash)
        {
            return true;
        }


        return animator.IsInTransition(0) &&
               animator.GetNextAnimatorStateInfo(0).shortNameHash == slamStateHash;
    }
    
    public void AnimationEvent_SlamHit()
    {
        if(currentState != BossState.Slam)
        {
            return;
        }
        
        if(slamPhase == SlamPhase.Recover)
        {
            return;
        }

        // 动画落地帧以锁定点为唯一落点。即使帧率波动或动画事件间隔
        // 与配置略有误差，也不会出现 Boss 落在警告区域之外。
        MoveSlamToLockedTarget();
        slamMoving = false;

        slamPhase = SlamPhase.Recover;

        HideSlamWarning();

        Collider[] hits =Physics.OverlapSphere(slamTargetPosition,slamRadius);

        foreach(Collider hit in hits)
        {
            if(hit.CompareTag("Player"))
            {
                Health playerHealth = hit.GetComponent<Health>();

                if(playerHealth == null)
                {
                    continue;
                }


                DamageInfo damageInfo =
                    new DamageInfo(
                        slamDamage,
                        gameObject,
                        hit.transform.position,
                        Vector3.up,
                        Vector3.down
                    );


                playerHealth.TakeDamage(in damageInfo);
                
            }
        }


        // PlaySlamEffect();
    }
    
    private void HideSlamWarning()
    {
        if(slamWarningDecal == null)
        {
            return;
        }

        slamWarningDecal.enabled = false;
        slamWarningDecal.transform.SetParent(transform, false);
    }

    private void UpdateChase(float distanceToPlayer)
    {
        if(Time.time >= nextCombatDecisionTime)
        {
            nextCombatDecisionTime = Time.time + combatDecisionInterval;

            BossCombatAction selectedAction =SelectCombatAction(distanceToPlayer);

            switch(selectedAction)
            {
                case BossCombatAction.Melee:
                    EnterAttackState();
                    return;

                case BossCombatAction.Charge:
                    EnterChargeState();
                    return;

                case BossCombatAction.Slam:
                    EnterSlamState();
                    return;
            }
        }

        UpdateChaseMovement(distanceToPlayer);
    }

    /// <summary>
    /// 根据 Boss 与玩家当前的距离，从普通攻击、冲锋、砸地三个战斗动作中选择一个。
    /// 
    /// 选择流程：
    /// 1. 分别计算三个动作当前的 Utility 分数。
    /// 2. 找出最高分。
    /// 3. 只保留接近最高分的动作，分数过低的动作直接排除。
    /// 4. 在剩余候选动作中，根据分数作为权重进行随机选择。
    /// 
    /// 这样既能让 Boss 优先选择当前最合适的技能，
    /// 又不会每次都固定使用最高分技能，使战斗表现更加随机、自然。
    /// </summary>
    /// <param name="distanceToPlayer">Boss 与玩家之间的水平距离。</param>
    /// <returns>本次选择出的战斗动作，如果没有任何可用动作则返回 None。</returns>
    private BossCombatAction SelectCombatAction(float distanceToPlayer)
    {
        // 分别计算普通攻击、冲锋和砸地在当前情况下的适用分数。
        // 分数越高，说明这个动作当前越适合使用。
        float meleeScore = EvaluateMeleeUtility(distanceToPlayer);
        float chargeScore = EvaluateChargeUtility(distanceToPlayer);
        float slamScore = EvaluateSlamUtility(distanceToPlayer);

        // 找出三个动作中的最高分。
        float highestScore = Mathf.Max(meleeScore, Mathf.Max(chargeScore, slamScore));

        // 如果三个动作的分数都为 0，
        // 说明当前没有任何可以执行的攻击，例如技能都在冷却或距离不合适。
        if(highestScore <= 0f)
        {
            return BossCombatAction.None;
        }

        // 计算进入最终随机选择的最低分数。
        // 例如：
        // highestScore = 1
        // topScoreSelectionRatio = 0.85
        // 那么只有分数 >= 0.85 的动作才能参与最终选择。
        float selectionThreshold = highestScore * topScoreSelectionRatio;

        // 低于阈值的动作权重直接设为 0，相当于从候选列表中排除。
        // 达到阈值的动作，则直接使用自己的 Utility 分数作为随机权重。
        float meleeWeight = meleeScore >= selectionThreshold ? meleeScore : 0f;
        float chargeWeight = chargeScore >= selectionThreshold ? chargeScore : 0f;
        float slamWeight = slamScore >= selectionThreshold ? slamScore : 0f;

        // 计算所有候选动作的总权重。
        float totalWeight = meleeWeight + chargeWeight + slamWeight;

        // 在 0 ~ totalWeight 之间随机一个数，
        // 后面根据各个技能占据的权重区间决定最终选中哪个动作。
        float selection = Random.value * totalWeight;

        // 普通攻击占据第一个权重区间：
        // 0 ~ meleeWeight。
        if(selection < meleeWeight)
        {
            return BossCombatAction.Melee;
        }

        // 如果没有落入普通攻击区间，
        // 再减去冲锋权重，判断是否落入冲锋的权重区间。
        if(selection < meleeWeight + chargeWeight)
        {
            return BossCombatAction.Charge;
        }

        // 前两个都没有选中时，剩下的候选就是 Slam。
        return BossCombatAction.Slam;
    }

    private float EvaluateMeleeUtility(float distanceToPlayer)
    {
        if(Time.time < nextAttackTime ||
           distanceToPlayer > attackDistance ||
           attackDistance <= 0f)
        {
            return 0f;
        }

        float proximity = 1f - Mathf.Clamp01(
            distanceToPlayer / attackDistance
        );

        return meleeUtilityWeight * Mathf.Lerp(0.75f, 1f, proximity);
    }

    /// <summary>
    /// 计算当前情况下冲锋技能的 Utility 分数。
    /// 
    /// 主要考虑：
    /// 1. 冲锋是否处于冷却状态。
    /// 2. 特殊技能公共间隔是否结束。
    /// 3. 玩家是否处于适合冲锋的距离范围。
    /// 4. Boss 当前是否大致朝向玩家。
    /// 5. 玩家距离是否接近冲锋的最佳距离。
    /// 
    /// 最终返回一个分数：
    /// 分数越高，说明当前越适合使用冲锋。
    /// 如果当前不能使用冲锋，则直接返回 0。
    /// </summary>
    /// <param name="distanceToPlayer">Boss 与玩家之间的水平距离。</param>
    /// <returns>冲锋技能当前的 Utility 分数。</returns>
    private float EvaluateChargeUtility(float distanceToPlayer)
    {
        // 以下任意条件成立，都说明当前不能使用冲锋：
        // 1. 冲锋自身还在冷却。
        // 2. 上一个特殊技能结束后的公共等待时间还没有结束。
        // 3. 玩家距离太近。
        // 4. 玩家距离太远。
        if(Time.time < nextChargeTime || Time.time < nextSpecialSkillTime ||
           distanceToPlayer < chargeMinDistance || distanceToPlayer > chargeMaxDistance)
        {
            return 0f;
        }

        // 计算 Boss 指向玩家的方向。
        Vector3 toPlayer = player.position - transform.position;

        // 冲锋只考虑水平面的方向，因此忽略 Y 轴高度差。
        toPlayer.y = 0f;

        // 如果 Boss 和玩家几乎处于同一个位置，
        // 就无法得到有效的冲锋方向。
        if(toPlayer.sqrMagnitude < 0.001f)
        {
            return 0f;
        }

        // 计算 Boss 当前朝向和玩家方向之间的点积。
        //
        // facingDot 越接近 1：
        // Boss 越正对着玩家。
        //
        // facingDot 越接近 0：
        // 玩家越偏向 Boss 的侧面。
        //
        // facingDot 小于 0：
        // 玩家已经处于 Boss 身后。
        float facingDot = Vector3.Dot(transform.forward, toPlayer.normalized);

        // 如果 Boss 当前朝向玩家的程度太低，
        // 就不允许发动冲锋。
        //
        // 这样可以避免 Boss 明明背对着玩家，
        // 却突然瞬间转身发动冲锋。
        if(facingDot < chargeMinFacingDot)
        {
            return 0f;
        }

        // 冲锋路线被墙体、场景碰撞体或 NavMesh 边界阻挡时，
        // 该技能本轮不参与选择。
        if(IsChargePathBlocked(player.position))
        {
            return 0f;
        }

        // 根据玩家当前距离计算“距离适合度”。
        //
        // chargeMinDistance：
        // 冲锋允许的最近距离。
        //
        // chargePreferredDistance：
        // 冲锋最理想的距离，在这里分数最高。
        //
        // chargeMaxDistance：
        // 冲锋允许的最远距离。
        //
        // 玩家越接近 preferredDistance，distanceScore 越接近 1。
        // 越接近最小/最大边界，distanceScore 越低。
        float distanceScore = CalculateBandUtility(distanceToPlayer, chargeMinDistance, chargePreferredDistance, chargeMaxDistance);

        // 把 facingDot 映射到 0 ~ 1。
        //
        // facingDot == chargeMinFacingDot 时：
        // facingScore = 0。
        //
        // facingDot == 1 时：
        // facingScore = 1。
        //
        // 也就是说 Boss 越正对玩家，朝向评分越高。
        float facingScore = Mathf.InverseLerp(chargeMinFacingDot, 1f, facingDot);

        // 计算冲锋最终基础分数。
        //
        // distanceScore：
        // 当前距离是否适合冲锋。
        //
        // Mathf.Lerp(0.65f, 1f, facingScore)：
        // 根据 Boss 朝向玩家的程度给分数增加一个倍率。
        // 即使刚刚达到最低朝向要求，也保留 65% 分数；
        // 完全正对玩家时则获得 100% 分数。
        //
        // chargeUtilityWeight：
        // 冲锋整体的技能权重，可以用来调整冲锋相对于其他技能的优先级。
        float score = distanceScore * Mathf.Lerp(0.65f, 1f, facingScore) * chargeUtilityWeight;

        // 如果 Boss 上一次使用的特殊技能也是 Charge，
        // 则给这次冲锋分数施加重复技能惩罚。
        //
        // 用来降低连续两次使用同一个特殊技能的概率。
        return ApplyRepeatSkillPenalty(BossCombatAction.Charge, score);
    }

    private float EvaluateSlamUtility(float distanceToPlayer)
    {
        if(Time.time < nextSlamTime ||
           Time.time < nextSpecialSkillTime ||
           distanceToPlayer < slamMinDecisionDistance ||
           distanceToPlayer > slamDistance)
        {
            return 0f;
        }

        // Slam 必须拥有合法 NavMesh 落点，并且起点到落点之间
        // 不能被高墙阻挡。检测高度以下的低矮障碍允许跳过。
        if(!TryGetSlamTargetPosition(out Vector3 candidateTarget) ||
           IsSlamPathBlockedByWall(candidateTarget))
        {
            return 0f;
        }

        float score = CalculateBandUtility(
            distanceToPlayer,
            slamMinDecisionDistance,
            slamPreferredDistance,
            slamDistance
        ) * slamUtilityWeight;

        return ApplyRepeatSkillPenalty(
            BossCombatAction.Slam,
            score
        );
    }

    /// <summary>
    /// 检查 Boss 从当前位置冲锋到目标位置的路径是否被阻挡。
    ///
    /// 检测分为两层：
    /// 1. NavMesh 检测：判断目标点是否可达，以及中间是否存在不可通行区域、断层或 Carve 障碍。
    /// 2. Collider 检测：判断路径上是否存在没有反映到 NavMesh 中的实体墙体。
    ///
    /// 只要任意一种检测认为路径不可通过，就返回 true。
    /// </summary>
    /// <param name="targetPosition">本次冲锋准备前往的目标位置。</param>
    /// <returns>true 表示冲锋路径被阻挡，false 表示路径可以冲锋。</returns>
    private bool IsChargePathBlocked(Vector3 targetPosition)
    {
        // NavMeshAgent 不存在、被禁用，或者当前 Boss 不在 NavMesh 上，
        // 都无法进行可靠的路径检测，因此直接认为路径不可用。
        if(agent == null || !agent.enabled || !agent.isOnNavMesh)
        {
            return true;
        }

        // 计算 Boss 当前所在位置指向目标位置的方向。
        Vector3 direction = targetPosition - transform.position;

        // 冲锋只考虑 XZ 水平面，因此忽略 Y 轴高度差。
        direction.y = 0f;

        // 获取 Boss 到目标位置之间的水平距离。
        float distance = direction.magnitude;

        // 如果距离几乎为 0，说明没有有效的冲锋方向，
        // 因此认为这条冲锋路径不可用。
        if(distance <= 0.001f)
        {
            return true;
        }

        // 在目标位置附近寻找一个合法的 NavMesh 点。
        //
        // targetPosition：
        // 原本准备冲向的位置。
        //
        // skillPathSampleRadius：
        // 允许在目标点周围多大的范围内寻找 NavMesh。
        //
        // agent.areaMask：
        // 只寻找 Boss 当前 NavMeshAgent 可以行走的区域。
        //
        // 如果找不到合法 NavMesh 点，说明目标位置本身不可到达。
        if(!NavMesh.SamplePosition(targetPosition, out NavMeshHit sampledTarget, skillPathSampleRadius, agent.areaMask))
        {
            return true;
        }

        // 从 Boss 当前所在的 NavMesh 位置向目标 NavMesh 点进行射线检测。
        //
        // 如果 NavMesh.Raycast 返回 true，
        // 说明这两个位置之间存在 NavMesh 边界、断层，
        // 或者已经通过 NavMeshObstacle Carve 切出来的不可通行区域。
        if(NavMesh.Raycast(agent.nextPosition, sampledTarget.position, out _, agent.areaMask))
        {
            return true;
        }

        // NavMesh 路径没有问题后，再使用胶囊体检测实体 Collider。
        //
        // direction / distance：
        // 将方向向量归一化，得到冲锋方向。
        //
        // distance：
        // 检测距离，也就是 Boss 到目标位置之间的距离。
        //
        // 这一层主要负责发现：
        // 有 Collider，但没有正确反映到 NavMesh 上的墙体或其他障碍物。
        //
        // 找到障碍物返回 true，
        // 没找到障碍物返回 false。
        return TryGetNearestChargeObstacle(direction / distance, distance, out _);
    }

    private bool IsSlamPathBlockedByWall(Vector3 targetPosition)
    {
        Vector3 origin = transform.position + Vector3.up * slamWallCheckHeight;
                         
        Vector3 target = targetPosition;
        target.y = origin.y;

        Vector3 direction = target - origin;
        float distance = direction.magnitude;

        if(distance <= 0.001f)
        {
            return false;
        }

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            slamWallCheckRadius,
            direction / distance,
            skillObstacleHits,
            distance,
            skillObstacleMask,
            QueryTriggerInteraction.Ignore
        );

        return HasRelevantObstacleHit(hitCount, out _);
    }

    /// <summary>
    /// 检测 Boss 沿指定方向冲锋时，前方是否存在障碍物。
    /// 如果检测到多个障碍物，会返回距离 Boss 最近的障碍物距离。
    /// </summary>
    /// <param name="direction">冲锋方向，通常是已经归一化后的 chargeDirection。</param>
    /// <param name="distance">本次向前检测的最大距离。</param>
    /// <param name="nearestDistance">如果检测到障碍物，返回最近障碍物距离 Boss 的距离。</param>
    /// <returns>检测到有效障碍物返回 true，否则返回 false。</returns>
    private bool TryGetNearestChargeObstacle(Vector3 direction,float distance,out float nearestDistance)
    {
        // 根据 NavMeshAgent 的半径、高度等参数，
        // 计算出一个大致包住 Boss 身体的胶囊体。
        //
        // bottom：胶囊体下端球心位置。
        // top：胶囊体上端球心位置。
        // radius：胶囊体半径。
        //
        // 后面不会只用一根射线检测，
        // 而是让这个“Boss 身体大小的胶囊体”整体向前检测，
        // 可以避免 Boss 身体边缘撞墙时，中心射线却没有检测到的问题。
        GetChargeCapsule(out Vector3 bottom,out Vector3 top,out float radius);

        // 从当前 Boss 所在位置开始，
        // 让上面计算出的胶囊体沿 direction 方向向前扫描 distance 距离。
        //
        // 可以简单理解成：
        //
        //      Boss胶囊体
        //        ╭──╮
        //        │  │ ====================>
        //        │  │       direction
        //        ╰──╯
        //
        // 如果前方存在墙、柱子等 Collider，
        // 就会被记录到 skillObstacleHits 数组中。
        int hitCount = Physics.CapsuleCastNonAlloc(
            bottom,
            top,
            radius,
            direction,
            skillObstacleHits,
            distance,
            skillObstacleMask,
            QueryTriggerInteraction.Ignore
        );

        // CapsuleCast 检测到的 Collider 里面，
        // 可能包含 Boss 自己、Boss 的子物体、玩家等，
        // 这些并不应该被当成真正的冲锋障碍物。
        //
        // HasRelevantObstacleHit 会进一步过滤这些无效结果，
        // 并从剩余的真正障碍物中找到距离 Boss 最近的一个，
        // 将距离写入 nearestDistance。
        return HasRelevantObstacleHit(hitCount, out nearestDistance);
    }

    private bool HasRelevantObstacleHit(
        int hitCount,
        out float nearestDistance)
    {
        nearestDistance = float.PositiveInfinity;
        bool foundObstacle = false;

        for(int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = skillObstacleHits[i].collider;
            if(hitCollider == null)
            {
                continue;
            }

            Transform hitTransform = hitCollider.transform;
            bool belongsToBoss =
                hitTransform == transform ||
                hitTransform.IsChildOf(transform);
            bool belongsToPlayer =
                player != null &&
                (hitTransform == player ||
                 hitTransform.IsChildOf(player));

            if(belongsToBoss || belongsToPlayer)
            {
                continue;
            }

            foundObstacle = true;
            nearestDistance = Mathf.Min(
                nearestDistance,
                skillObstacleHits[i].distance
            );
        }

        return foundObstacle;
    }

    private void GetChargeCapsule(
        out Vector3 bottom,
        out Vector3 top,
        out float radius)
    {
        radius = Mathf.Max(
            0.05f,
            agent.radius * chargeObstacleRadiusScale
        );

        float height = Mathf.Max(agent.height, radius * 2f);
        Vector3 center = transform.position + Vector3.up *
            (agent.baseOffset + height * 0.5f);
        float halfSegment = Mathf.Max(
            0f,
            height * 0.5f - radius
        );

        bottom = center - Vector3.up * halfSegment;
        top = center + Vector3.up * halfSegment;
    }

    /// <summary>
    /// 根据一个数值在“最小值 → 最佳值 → 最大值”区间中的位置，计算对应的 Utility 分数。
    ///
    /// 评分规则：
    /// minimum 位置分数为 0。
    /// preferred 位置分数为 1。
    /// maximum 位置分数重新下降到 0。
    ///
    /// 整体形成一个类似三角形的评分曲线：
    ///
    /// minimum        preferred        maximum
    ///    0  ----------  1  ----------  0
    ///
    /// 数值越接近 preferred，返回的分数越高。
    /// 如果数值超出 minimum ~ maximum 范围，则直接返回 0。
    /// </summary>
    /// <param name="value">当前需要进行评分的数值。</param>
    /// <param name="minimum">允许范围的最小值，此位置 Utility 为 0。</param>
    /// <param name="preferred">最理想的数值，此位置 Utility 为 1。</param>
    /// <param name="maximum">允许范围的最大值，此位置 Utility 为 0。</param>
    /// <returns>0 ~ 1 之间的 Utility 分数。</returns>
    private static float CalculateBandUtility(float value, float minimum, float preferred, float maximum)
    {
        // 如果最大值和最小值几乎相同，说明区间无效。
        // 如果 value 已经超出允许范围，也没有评分意义。
        if(maximum - minimum <= 0.001f || value < minimum || value > maximum)
        {
            return 0f;
        }

        // 确保 preferred 一定处于 minimum 和 maximum 之间。
        //
        // 这里额外保留 0.001f 的距离，
        // 是为了防止 preferred 和 minimum / maximum 完全重合，
        // 从而避免后面的 InverseLerp 出现无效区间。
        preferred = Mathf.Clamp(preferred, minimum + 0.001f, maximum - 0.001f);

        // value 位于 minimum → preferred 这一段时，
        // 分数从 0 逐渐上升到 1。
        //
        // value == minimum  → 0
        // value == preferred → 1
        if(value <= preferred)
        {
            return Mathf.InverseLerp(minimum, preferred, value);
        }

        // value 位于 preferred → maximum 这一段时，
        // 分数从 1 逐渐下降到 0。
        //
        // 这里故意把 InverseLerp 的参数顺序反过来：
        // value == preferred → 1
        // value == maximum   → 0
        return Mathf.InverseLerp(maximum, preferred, value);
    }

    private float ApplyRepeatSkillPenalty(
        BossCombatAction action,
        float score)
    {
        return lastSpecialAction == action
            ? score * repeatSkillPenalty
            : score;
    }

    private void MarkSpecialActionStarted(BossCombatAction action)
    {
        lastSpecialAction = action;
    }

    private void ScheduleSpecialSkillGap()
    {
        nextSpecialSkillTime = Time.time + specialSkillGap;
    }

    private void UpdateChaseMovement(float distanceToPlayer)
    {
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
    
    private void EnterSlamState()
    {
        // 选择和真正进入技能之间场景可能已经变化，因此在提交技能前复查。
        if(!TryGetSlamTargetPosition(out slamTargetPosition) ||
           IsSlamPathBlockedByWall(slamTargetPosition))
        {
            EnterChaseState();
            return;
        }

        currentState = BossState.Slam;

        agent.isStopped = true;
        agent.ResetPath();
        
        ShowSlamWarning();

        Vector3 direction = slamTargetPosition - transform.position;
        direction.y = 0f;

        if(direction.sqrMagnitude > 0.001f)
        {
            slamDirection = direction.normalized;
            transform.rotation = Quaternion.LookRotation(
                slamDirection,
                Vector3.up
            );
        }

        slamPhase = SlamPhase.Windup;

        slamEndTime = Time.time + slamWindupDuration;

        slamMoving = false;
        slamAnimationEntered = false;
        slamMoveStartPosition = transform.position;
        slamMoveElapsed = 0f;
        slamMoveDuration = slamJumpTravelDuration;

        OverrideSlamRootMotion();
        bossAnimationController.SetSlam();

        nextSlamTime = Time.time + slamCooldown;
        MarkSpecialActionStarted(BossCombatAction.Slam);
    }
    
    private void ShowSlamWarning()
    {
        if(slamWarningDecal == null)
        {
            return;
        }

        Transform decalTransform = slamWarningDecal.transform;

        // 与冲锋警告使用相同流程：先作为 Boss 子物体设置局部变换，
        // 再脱离层级并保留世界坐标，使锁定后的警告区不再跟随 Boss。
        decalTransform.SetParent(transform, false);
        decalTransform.localPosition =transform.InverseTransformPoint(slamTargetPosition) +Vector3.up * slamWarningHeight;
                                      
        decalTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        slamWarningDecal.size = new Vector3(
            slamRadius * 2f,
            slamRadius * 2f,
            Mathf.Max(0.1f, slamWarningProjectionDepth)
        );

        decalTransform.SetParent(null, true);
        slamWarningDecal.enabled = true;
    }

    private void OverrideSlamRootMotion()
    {
        if(animator == null || slamRootMotionOverridden)
        {
            return;
        }

        slamPreviousApplyRootMotion = animator.applyRootMotion;
        animator.applyRootMotion = false;
        slamRootMotionOverridden = true;
    }

    private void RestoreSlamRootMotion()
    {
        if(animator == null || !slamRootMotionOverridden)
        {
            return;
        }

        animator.applyRootMotion = slamPreviousApplyRootMotion;
        slamRootMotionOverridden = false;
    }

    /// <summary>
    /// 尝试获取本次 Slam 的合法 NavMesh 落点。
    /// 找不到落点时不再使用玩家坐标强行跳跃，避免穿墙或落到 NavMesh 外。
    /// </summary>
    private bool TryGetSlamTargetPosition(out Vector3 targetPosition)
    {
        Vector3 requestedPosition = player.position;

        if(agent != null &&
           agent.enabled &&
           NavMesh.SamplePosition(
               requestedPosition,
               out NavMeshHit hit,
               slamTargetSampleRadius,
               agent.areaMask))
        {
            targetPosition = hit.position;
            return true;
        }

        requestedPosition.y = transform.position.y;
        targetPosition = requestedPosition;
        return false;
    }

    private void MoveSlamToLockedTarget()
    {
        if(agent != null && agent.enabled && agent.isOnNavMesh)
        {
            // 锁定点已经通过 NavMesh.SamplePosition 取得，Warp 可确保落地帧
            // Boss、伤害中心和警告区域使用完全相同的位置。
            if(agent.Warp(slamTargetPosition))
            {
                return;
            }
        }

        Vector3 fallbackPosition = slamTargetPosition;
        fallbackPosition.y = transform.position.y;
        transform.position = fallbackPosition;
    }
    
    private void ShowChargeWarning()
    {
        if (chargeWarningDecal == null)
        {
            return;
        }

        Transform decalTransform = chargeWarningDecal.transform;

        decalTransform.SetParent(transform, false);
        decalTransform.localPosition = new Vector3(0f, chargeWarningHeight, chargeRemainingDistance * 0.5f);
        decalTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        chargeWarningDecal.size = new Vector3(chargeWarningWidth, chargeRemainingDistance, chargeWarningProjectionDepth);

        decalTransform.SetParent(null, true);
        chargeWarningDecal.enabled = true;
    }

    /// <summary>
    /// 隐藏警告图案
    /// </summary>
    private void HideChargeWarning()
    {
        if (chargeWarningDecal == null)
        {
            return;
        }

        chargeWarningDecal.enabled = false;
        chargeWarningDecal.transform.SetParent(transform, false);
        
        // 冲锋动画结束
        bossAnimationController.SetCharging(false);
    }
    
    /// <summary>
    /// 进入冲锋状态
    /// </summary>
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

        // 蓄力前再次确认路线，避免评分后障碍状态变化。
        if(IsChargePathBlocked(player.position))
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
        
        // 显示警告图案
        ShowChargeWarning();
        
        bossAnimationController.SetCharging(true);

        // 首先进入冲锋的蓄力阶段。
        chargePhase = ChargePhase.Windup;

        // 记录蓄力结束时间。
        chargePhaseEndTime = Time.time + chargeWindupDuration;

        // 从这一刻开始计算下一次冲锋冷却。
        nextChargeTime = Time.time + chargeCooldown;
        MarkSpecialActionStarted(BossCombatAction.Charge);

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
    
    /// <summary>
    /// 更新 Boss 冲锋移动阶段。
    ///
    /// 每一帧主要做四件事：
    /// 1. 计算这一帧理论上应该移动的距离。
    /// 2. 检查前方是否存在 Collider 或 NavMesh 障碍。
    /// 3. 按允许的最大距离移动 Boss。
    /// 4. 扣除实际移动距离，并判断冲锋是否结束。
    /// </summary>
    private void UpdateChargeMoving()
    {
        // 如果 Boss 已经不在 NavMesh 上，
        // 就无法继续通过 NavMeshAgent 正常冲锋，
        // 直接结束冲锋并进入恢复阶段。
        if(!agent.isOnNavMesh)
        {
            BeginChargeRecover();
            return;
        }

        // 计算这一帧理论上应该移动的距离。
        //
        // chargeSpeed * Time.deltaTime：
        // 按当前冲锋速度计算这一帧应该前进多少米。
        //
        // chargeRemainingDistance：
        // 当前整个冲锋还剩多少距离。
        //
        // 取两者较小值，防止最后一帧冲过终点。
        float requestedDistance = Mathf.Min(chargeSpeed * Time.deltaTime, chargeRemainingDistance);

        // 在真正移动之前先用胶囊体检测前方是否存在 Collider 障碍。
        //
        // 多检测一个 skillObstacleSkin，
        // 是为了提前发现墙体，并让 Boss 最终停在墙前留出一点安全距离。
        //
        // blocked：
        // true 表示前方存在障碍物。
        //
        // obstacleDistance：
        // Boss 到最近障碍物之间的距离。
        bool blocked = TryGetNearestChargeObstacle(chargeDirection, requestedDistance + skillObstacleSkin, out float obstacleDistance);

        // 根据是否检测到障碍，计算这一帧真正允许移动的距离。
        //
        // 没有障碍：
        // 直接移动 requestedDistance。
        //
        // 有障碍：
        // 最多移动到障碍物前面，并额外保留 skillObstacleSkin 的距离。
        //
        // Mathf.Max(0f, ...) 防止结果变成负数。
        float allowedDistance = blocked ? Mathf.Max(0f, obstacleDistance - skillObstacleSkin) : requestedDistance;

        // 记录 Boss 当前在 NavMesh 上的位置。
        Vector3 navMeshStart = agent.nextPosition;

        // 根据冲锋方向和允许移动距离，
        // 计算这一帧理论上的终点。
        Vector3 requestedEnd = navMeshStart + chargeDirection * allowedDistance;

        // Collider 检测通过以后，再检查 NavMesh。
        //
        // NavMesh.Raycast 返回 true，
        // 说明 Boss 到 requestedEnd 之间遇到了 NavMesh 边界、
        // 不可通行区域、断层或被 Carve 的障碍。
        if(NavMesh.Raycast(navMeshStart, requestedEnd, out NavMeshHit navMeshHit, agent.areaMask))
        {
            // navMeshHit.position 是射线碰到 NavMesh 边界的位置。
            //
            // Boss 最多移动到这个位置之前，
            // 同样保留 skillObstacleSkin 的安全距离。
            allowedDistance = Mathf.Max(0f, GetFlatDistance(navMeshStart, navMeshHit.position) - skillObstacleSkin);

            // 标记这一帧已经碰到障碍，
            // 后面移动完成以后结束冲锋。
            blocked = true;
        }

        // 保存移动前的位置。
        //
        // 后面不用 allowedDistance 直接扣除剩余距离，
        // 而是根据移动前后的位置计算 Boss 实际移动了多少。
        Vector3 positionBeforeMove = agent.nextPosition;

        // 只有确实还有可移动距离时才执行移动。
        if(allowedDistance > 0f)
        {
            agent.Move(chargeDirection * allowedDistance);
        }

        // 计算 Boss 这一帧真正移动的距离。
        //
        // 使用实际移动距离而不是 requestedDistance，
        // 可以避免撞墙以后仍然错误地扣除整帧冲锋距离。
        float actualMoveDistance = GetFlatDistance(positionBeforeMove, agent.nextPosition);

        // Boss 移动完成以后检查是否撞到了玩家，
        // 如果进入命中范围则造成一次冲锋伤害。
        TryDealChargeDamage();

        // 从剩余冲锋距离中扣除这一帧真正移动的距离。
        chargeRemainingDistance -= actualMoveDistance;

        // 如果这一帧没有撞到障碍，
        // 并且冲锋距离还没有跑完，
        // 就保持 Moving 状态，下一帧继续执行。
        if(!blocked && chargeRemainingDistance > 0.01f)
        {
            return;
        }

        // 出现以下任意情况时结束冲锋：
        // 1. 前方碰到了 Collider 障碍。
        // 2. 前方碰到了 NavMesh 边界。
        // 3. chargeRemainingDistance 已经耗尽。
        //
        // 然后进入冲锋后的恢复/硬直阶段。
        BeginChargeRecover();
    }

    private void BeginChargeRecover()
    {
        chargeRemainingDistance = 0f;
        chargePhase = ChargePhase.Recover;
        chargePhaseEndTime = Time.time + chargeRecoverDuration;
        HideChargeWarning();
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

        // 关闭冲刺动画
        bossAnimationController.SetCharging(false);

        ScheduleSpecialSkillGap();

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
            EnterChaseState();
            return;
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
        nextCombatDecisionTime = Time.time + combatDecisionInterval;
    }

    private void EnterAttackState()
    {
        currentState = BossState.Attack;

        agent.isStopped = true;
        agent.ResetPath();
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
