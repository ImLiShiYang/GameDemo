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

    // 下一次允许进行普通攻击的时间。
    // 通过 Time.time >= nextAttackTime 判断普通攻击冷却是否结束。
    private float nextAttackTime;

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
            health.Died += HandleDied;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= HandleDied;
        }

        RestoreSlamRootMotion();
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
        // 玩家已经进入普通攻击范围，优先进行普通攻击。
        if (distanceToPlayer <= attackDistance)
        {
            EnterAttackState();
            return;
        }

        // 冲锋冷却结束，并且玩家处于适合冲锋的距离范围内，
        // 就停止普通追击，进入 Charge 状态。
        // if (Time.time >= nextChargeTime && distanceToPlayer >= chargeMinDistance && distanceToPlayer <= chargeMaxDistance)
        // {
        //     EnterChargeState();
        //     return;
        // }

        if(Time.time >= nextSlamTime && distanceToPlayer <= slamDistance)
        {
            EnterSlamState();
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
    
    private void EnterSlamState()
    {
        currentState = BossState.Slam;

        agent.isStopped = true;
        agent.ResetPath();

        slamTargetPosition = GetSlamTargetPosition();
        
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
    /// 获取本次 Slam 攻击锁定的落点。
    /// 优先使用玩家当前所在位置，并在玩家附近寻找一个合法的 NavMesh 位置。
    /// 如果找不到合适的 NavMesh 点，则使用玩家当前的水平位置作为备用落点。
    /// </summary>
    /// <returns>本次 Slam 攻击最终锁定的世界坐标。</returns>
    private Vector3 GetSlamTargetPosition()
    {
        // 先记录进入 Slam 时玩家当前的位置。
        // 这个位置只在 Slam 开始时获取一次，之后玩家移动不会再修改本次 Slam 的落点。
        Vector3 requestedPosition = player.position;

        // 尝试在玩家当前位置附近寻找一个合法的 NavMesh 点。
        // slamTargetSampleRadius 表示搜索半径，
        // agent.areaMask 表示只搜索当前 Boss 的 NavMeshAgent 可以行走的区域。
        if(agent != null && agent.enabled && NavMesh.SamplePosition(requestedPosition,out NavMeshHit hit,slamTargetSampleRadius,agent.areaMask))
        {
            // 找到了合法的 NavMesh 位置，
            // 将这个位置作为本次 Slam 最终锁定的落点。
            return hit.position;
        }

        // 如果玩家附近没有找到合法的 NavMesh 点，
        // 就继续使用玩家当前的 X、Z 坐标。
        //
        // Y 坐标改成 Boss 当前高度，
        // 避免玩家和 Boss 存在高度差时影响后续只在水平面进行的 Slam 移动。
        requestedPosition.y = transform.position.y;

        // 返回备用落点。
        return requestedPosition;
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
