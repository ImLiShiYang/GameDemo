using UnityEngine;
using UnityEngine.Animations.Rigging;
using UnityEngine.Serialization;

/// <summary>
/// 第三人称俯视角角色控制器：
/// 1. WASD 相对相机方向移动。
/// 2. 使用 MoveX / MoveY 驱动包含 Idle、前、后、左、右的二维 Blend Tree。
/// 3. 鼠标通过玩家根节点上的稳定水平面提供瞄准方向。
/// 4. 玩家根节点负责身体朝向，Animation Rigging 负责枪和双手对准 AimTarget。
/// 5. Rig 求解后从真实枪口方向发射射线：命中物体时 AimPoint 为命中点，
///    未命中时 AimPoint 为枪口前方的远点。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class GrayboxPlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 3.2f;
    [SerializeField] private float turnSpeed = 12f;
    [SerializeField] private float acceleration = 18f;

    [Header("Locomotion Animation")]
    [Tooltip("MoveX / MoveY 参数的平滑时间。")]
    [SerializeField] private float animationDampTime = 0.08f;

    [Tooltip("移动向量小于这个值时，将动画参数归零并播放 Idle。")]
    [SerializeField] private float animationIdleThreshold = 0.001f;

    [Header("Mouse Direction Aim")]
    [SerializeField] private Camera aimCamera;

    // 兼容旧字段名，替换脚本后尽量保留 Inspector 中原来的 LayerMask 设置。
    [FormerlySerializedAs("aimGroundMask")]
    [SerializeField] private LayerMask aimHitMask = ~0;

    [Tooltip("枪口射线最远检测距离。")]
    [SerializeField] private float aimRayDistance = 500f;

    [Tooltip("射线起点沿枪口方向向前偏移的距离，用于减少击中自身。")]
    [SerializeField] private float aimRayStartOffset = 0.05f;

    [Tooltip("枪方向参考点缺失时，角色根节点朝向鼠标的旋转速度。")]
    [SerializeField] private float aimTurnSpeed = 1080f;

    [Tooltip("鼠标进入角色中心这个水平半径后，保持最后一次稳定的瞄准方向。")]
    [SerializeField, Min(0f)] private float mouseAimDeadZoneRadius = 0.35f;

    [Tooltip("进入死区后，鼠标必须额外离开这段距离才会恢复瞄准，防止在边缘反复切换。")]
    [SerializeField, Min(0f)] private float mouseAimDeadZoneHysteresis = 0.15f;

    [Tooltip("鼠标射线使用的稳定水平面高度，相对于玩家根节点。不要使用会被动画带动的枪口高度。")]
    [SerializeField] private float aimPlaneHeight = 1.4f;

    [Tooltip("AimTarget 与枪口的最小安全距离，只用于避免目标与枪口完全重合导致方向为零。")]
    [SerializeField, Min(0.01f)] private float weaponAimTargetMinDistance = 0.05f;

    [Header("Weapon Aim")]
    [Tooltip("枪口位置，同时作为物理射线和瞄准线的起点。")]
    [SerializeField] private Transform muzzle;
    [Tooltip("枪身后方参考点。")]
    [SerializeField] private Transform weaponAimStart;
    [Tooltip("枪口前方参考点。Start 指向 End 的方向必须沿着子弹发射方向。")]
    [SerializeField] private Transform weaponAimEnd;
    [Tooltip("枪械瞄准旋转节点。通过旋转这个节点，让真实枪管方向对准 AimTarget。")]
    [SerializeField] private Transform weaponAimPivot;
    [Tooltip("武器根节点。正常状态独立于手骨骼，翻滚/受击/死亡时由右手动画驱动。")]
    [SerializeField] private Transform weaponSocket;

    [Tooltip("角色右手骨骼。")]
    [SerializeField] private Transform rightHandBone;

    [Tooltip("枪上的右手握持点。")]
    [SerializeField] private Transform rightHandGrip;

    [Tooltip("枪上的左手握持点。")]
    [SerializeField] private Transform leftHandGrip;

    [Tooltip("右手 Two Bone IK 使用的 Target。")]
    [SerializeField] private Transform rightHandTarget;

    [Tooltip("左手 Two Bone IK 使用的 Target。")]
    [SerializeField] private Transform leftHandTarget;
    

    [Header("Animation Rigging")]
    [Tooltip("控制枪和双臂的 Rig。未设置时会自动查找名为 AimRig 的子物体。")]
    [SerializeField] private Rig aimRig;

    [Tooltip("Multi-Aim 使用的目标。未设置时会自动查找名为 AimTarget 的子物体。")]
    [SerializeField] private Transform aimRigTarget;

    [Tooltip("进入翻滚或受击时，Rig 权重淡出的速度；恢复操作时按相同速度淡入。")]
    [SerializeField, Min(0f)] private float aimRigBlendSpeed = 10f;

    [Header("Aim Line")]
    [SerializeField] private bool showAimLine = true;
    [SerializeField] private Color aimLineColor = new Color(0f, 1f, 0.8f, 1f);
    [SerializeField] private float aimLineWidth = 0.04f;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Animator animator;
    [SerializeField] private GamePlayerAttack playerAttack;

    [Header("Dodge Roll")]
    [SerializeField] private KeyCode rollKey = KeyCode.LeftShift;
    [Tooltip("两次翻滚之间的最短间隔。")]
    [SerializeField] private float rollCooldown = 0.35f;
    [Tooltip("小于这个输入强度时，认为角色没有移动输入。")]
    [SerializeField] private float rollInputThreshold = 0.05f;
    [Tooltip("Animator 中翻滚 Trigger 参数的名字。")]
    [SerializeField] private string rollTriggerName = "Roll";
    [Tooltip("翻滚结束后，角色重新朝向鼠标时的旋转速度，单位为度/秒。")]
    [SerializeField] private float postRollAimTurnSpeed = 540f;
    [Tooltip("与鼠标方向小于这个角度时，认为转向恢复完成。")]
    [SerializeField] private float postRollAimFinishAngle = 1f;
    
    [Header("Roll Invincibility")]
    [Tooltip("当前是否处于翻滚无敌帧。由翻滚动画事件控制。")]
    [SerializeField] private bool isInvincible;

    public bool IsInvincible => isInvincible;

    private float moveSpeedMultiplier = 1f;

    private Transform weaponSocketDefaultParent;
    private Vector3 weaponSocketDefaultLocalPosition;
    private Quaternion weaponSocketDefaultLocalRotation;
    private Vector3 weaponSocketDefaultLocalScale;
    
    private bool waitForAimMouseMovement;
    private Vector3 aimResumeMousePosition;
    
    public void WaitForAimMouseMovement()
    {
        aimResumeMousePosition = Input.mousePosition;
        waitForAimMouseMovement = true;
    }

    public void SetMoveSpeedMultiplier(float multiplier)
    {
        moveSpeedMultiplier = Mathf.Max(0f, multiplier);
    }
    
    private bool isRecoveringAimAfterRoll;

    private Vector3 moveInputDirection;
    private bool isRolling;
    private float nextRollAllowedTime;
    private float rollVerticalVelocity;
    private int rollTriggerHash;

    public bool IsRolling => isRolling;

    private CharacterController characterController;
    private Vector3 currentMoveDirection;
    private bool lockstepMovementEnabled;
    private Vector3 lockstepMoveDirection;

    /// <summary>
    /// 帧同步启用后，普通 Update 仍负责瞄准、射击和动画，但不再直接修改 XZ 位置。
    /// </summary>
    public bool LockstepMovementEnabled => lockstepMovementEnabled;
    public bool CanAcceptLockstepMovementInput => enabled && !isHitStunned && Time.timeScale > 0f;
    public float LockstepMoveSpeed => walkSpeed * moveSpeedMultiplier;
    
    // 缓存 RaycastNonAlloc 的结果，避免每帧创建新数组。
    private readonly RaycastHit[] aimHits = new RaycastHit[16];

    private LineRenderer aimLine;
    private Material aimLineMaterial;

    private bool isFiring;
    private bool waitForPrimaryFireRelease;
    private Vector3 lastAimDirection;
    private bool hasLastAimDirection;
    private bool hasCurrentAimDirection;
    private bool isMouseAimInsideDeadZone;
    private Vector3 weaponAimWorldPoint;
    private bool hasWeaponAimWorldPoint;
    
    public Vector3 WeaponAimWorldPoint => weaponAimWorldPoint;
    public bool HasWeaponAimWorldPoint => hasWeaponAimWorldPoint;
    
    private bool isHitStunned;

    public bool IsHitStunned => isHitStunned;

    /// <summary>
    /// 枪口射线最终指向的世界坐标。
    /// 命中物体时是碰撞点；未命中时是枪口前方远点。
    /// </summary>
    public Vector3 AimPoint { get; private set; }
    
    /// <summary>
    /// 鼠标射线与稳定瞄准水平面的世界空间交点。
    /// </summary>
    public Vector3 MouseAimWorldPoint { get; private set; }

    /// <summary>
    /// 当前是否成功获得鼠标水平面交点。
    /// </summary>
    public bool HasMouseAimWorldPoint { get; private set; }

    /// <summary>
    /// 当前是否拥有有效瞄准点。
    /// </summary>
    public bool HasAimPoint { get; private set; }
    
    public Vector3 AimOriginPosition
    {
        get
        {
            Transform aimOrigin = GetAimOriginTransform();
            return aimOrigin != null ? aimOrigin.position : transform.position;
        }
    }

    // Animator 中必须创建两个 Float 参数：MoveX 和 MoveY。
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (aimCamera == null && cameraTransform != null)
        {
            aimCamera = cameraTransform.GetComponent<Camera>();
        }

        if (aimCamera == null)
        {
            aimCamera = Camera.main;
        }
        
        if (playerAttack == null)
        {
            playerAttack = GetComponent<GamePlayerAttack>();
        }

        if (cameraTransform == null && aimCamera != null)
        {
            cameraTransform = aimCamera.transform;
        }

        ResolveAimRigReferences();
        
        if (weaponSocket != null)
        {
            weaponSocketDefaultParent = weaponSocket.parent;
            weaponSocketDefaultLocalPosition = weaponSocket.localPosition;
            weaponSocketDefaultLocalRotation = weaponSocket.localRotation;
            weaponSocketDefaultLocalScale = weaponSocket.localScale;
        }

        lastAimDirection = transform.forward;
        lastAimDirection.y = 0f;
        lastAimDirection = lastAimDirection.sqrMagnitude >= 0.0001f
            ? lastAimDirection.normalized
            : Vector3.forward;
        hasLastAimDirection = true;

        rollTriggerHash = Animator.StringToHash(rollTriggerName);

        CreateAimLine();
    }

    private void Update()
    {
        // 游戏暂停时，停止角色输入和射击逻辑。
        // 同时清空移动状态，防止暂停前的输入在恢复后继续残留。
        if (Time.timeScale <= 0f)
        {
            isFiring = false;
            waitForPrimaryFireRelease = true;
            moveInputDirection = Vector3.zero;
            currentMoveDirection = Vector3.zero;
            ResetLocomotionAnimator();
            return;
        }

        // 如果之前因为暂停等原因要求“必须先松开鼠标左键”，
        // 那么检测到左键已经松开后，就解除这个限制。
        if (waitForPrimaryFireRelease && !Input.GetMouseButton(0))
        {
            waitForPrimaryFireRelease = false;
        }

        /*
         * 受击硬直期间禁止：
         * 1. WASD 移动
         * 2. 射击
         * 3. 翻滚
         */
        if (isHitStunned)
        {
            // 受击时逐渐关闭 Animation Rigging，
            // 让角色回到受击动画本身的姿势，避免持枪 IK 干扰受击动画。
            UpdateAimRigWeight(false);

            // 停止射击。
            isFiring = false;

            // 清空输入方向和当前移动方向。
            moveInputDirection = Vector3.zero;
            currentMoveDirection = Vector3.zero;

            // 把 Animator 中 MoveX / MoveY 归零，
            // 防止受击时还混合播放移动动画。
            ResetLocomotionAnimator();

            /*
             * SimpleMove(Vector3.zero) 不产生水平移动，
             * 但 CharacterController 仍然会继续处理重力，
             * 防止角色在受击期间悬空。
             */
            if (characterController != null)
            {
                characterController.SimpleMove(Vector3.zero);
            }

            // 受击状态下，本帧后续移动、瞄准、翻滚、射击逻辑全部不执行。
            return;
        }

        // 每帧读取 WASD 输入，并转换为基于相机方向的世界空间移动方向。
        // 同时确保按下翻滚键的这一帧，可以直接取得当前移动方向。
        UpdateMoveInput();

        // 计算当前是否处于“持续射击”状态。
        // 必须同时满足：
        // 1. 当前没有翻滚
        // 2. 不处于“等待鼠标松开”状态
        // 3. 鼠标左键处于按住状态
        isFiring =
            !isRolling &&
            !waitForPrimaryFireRelease &&
            Input.GetMouseButton(0);

        // 当前没有翻滚，并且这一帧刚按下翻滚键时，
        // 尝试进入翻滚状态。
        if (!lockstepMovementEnabled && !isRolling && Input.GetKeyDown(rollKey))
        {
            TryStartRoll();
        }

        // 如果当前已经进入翻滚状态，
        // 就暂停普通移动和瞄准逻辑。
        if (isRolling)
        {
            // 翻滚期间逐渐关闭持枪 IK，
            // 避免双手 IK 与翻滚动画互相抢骨骼。
            UpdateAimRigWeight(false);

            // 停止普通移动。
            currentMoveDirection = Vector3.zero;

            // 清空移动 Blend Tree 参数，
            // 防止翻滚动画和走路动画同时混合。
            ResetLocomotionAnimator();

            // 翻滚期间后面的普通移动、瞄准和移动动画更新都不执行。
            return;
        }

        // 正常可操作状态下，逐渐把 Animation Rigging 权重恢复到 1，
        // 重新启用持枪和手臂 IK。
        UpdateAimRigWeight(true);

        // 根据当前输入更新 CharacterController 的移动。
        UpdateMovement();

        // 根据鼠标位置计算瞄准方向：
        // 1. 旋转角色根节点
        // 2. 更新 AimTarget
        // 供后续 Animation Rigging 使用。
        UpdateAimInputAndBodyRotation();

        // 根据角色当前实际移动方向，
        // 更新 Animator 中的 MoveX / MoveY 参数。
        UpdateLocomotionAnimator();
    }
    
    private void LateUpdate()
    {
        if (muzzle != null && aimRigTarget != null)
        {
            // 绿色：理论上枪应该指向的方向。
            Debug.DrawLine(muzzle.position, aimRigTarget.position, Color.green);
        }

        if (muzzle != null && weaponAimStart != null && weaponAimEnd != null)
        {
            // 蓝色：枪当前真实的枪管方向。
            Vector3 weaponDirection = (weaponAimEnd.position - weaponAimStart.position).normalized;
            Debug.DrawRay(muzzle.position, weaponDirection * 20f, Color.blue);
        }
        
        if (Time.timeScale <= 0f)
        {
            isFiring = false;
            return;
        }

        // 翻滚或受击期间已经关闭 IK，
        // 此时动画负责右手姿势，武器反过来跟随右手。
        if (isRolling || isHitStunned)
        {
            UpdateAimLine();
            return;
        }

        if (hasCurrentAimDirection)
        {
            UpdateAimPointFromWeaponRay(lastAimDirection);
        }
        else
        {
            HasAimPoint = false;
        }

        TryFire();
        UpdateAimLine();
    }
    
    private void RestoreWeaponNormalPose()
    {
        if (weaponSocket == null)
        {
            return;
        }

        if (weaponSocket.parent != weaponSocketDefaultParent)
        {
            weaponSocket.SetParent(weaponSocketDefaultParent, false);
        }

        weaponSocket.localPosition = weaponSocketDefaultLocalPosition;
        weaponSocket.localRotation = weaponSocketDefaultLocalRotation;
        weaponSocket.localScale = weaponSocketDefaultLocalScale;
    }

    private void UpdateMoveInput()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        moveInputDirection = GetCameraRelativeMoveDirection(horizontal, vertical);
    }

    public Vector3 GetCameraRelativeMoveDirection(float horizontal, float vertical)
    {

        Vector3 forward = cameraTransform != null
            ? cameraTransform.forward
            : transform.forward;

        Vector3 right = cameraTransform != null
            ? cameraTransform.right
            : transform.right;

        forward.y = 0f;
        right.y = 0f;

        if (forward.sqrMagnitude > 0.0001f)
        {
            forward.Normalize();
        }

        if (right.sqrMagnitude > 0.0001f)
        {
            right.Normalize();
        }

        Vector3 direction =
            forward * vertical +
            right * horizontal;

        return Vector3.ClampMagnitude(direction, 1f);
    }

    public void SetLockstepMovementEnabled(bool enabled)
    {
        lockstepMovementEnabled = enabled;
        lockstepMoveDirection = Vector3.zero;
        currentMoveDirection = Vector3.zero;
    }

    public void ApplyLockstepPose(Vector3 position, Vector3 confirmedMoveDirection)
    {
        lockstepMoveDirection = Vector3.ClampMagnitude(confirmedMoveDirection, 1f);

        Vector3 currentPosition = transform.position;
        transform.position = new Vector3(position.x, currentPosition.y, position.z);
    }
    
    public void PlayHitReaction()
    {
        if (animator == null)
        {
            return;
        }

        EnterAnimationDrivenWeaponState();
        
        isHitStunned = true;

        // 受击立即停止移动和射击
        isFiring = false;
        currentMoveDirection = Vector3.zero;
        moveInputDirection = Vector3.zero;

        // 如果正处于翻滚的非无敌阶段被打中，
        // 直接中断翻滚状态。
        isInvincible = false;

        ResetLocomotionAnimator();
        
        animator.ResetTrigger("Hit");
        animator.SetTrigger("Hit");
    }
    
    /// <summary>
    /// 由受击动画最后的 Animation Event 调用。
    /// 恢复玩家操作。
    /// </summary>
    public void FinishHitReaction()
    {
        isHitStunned = false;

        currentMoveDirection = Vector3.zero;
        moveInputDirection = Vector3.zero;
        
        RestoreWeaponNormalPose();
    }

    /// <summary>
    /// 立即清空移动 Blend Tree 参数。
    /// 翻滚期间防止走路动画继续和翻滚动画混合。
    /// </summary>
    private void ResetLocomotionAnimator()
    {
        if (animator == null)
        {
            return;
        }

        animator.SetFloat(MoveXHash, 0f);
        animator.SetFloat(MoveYHash, 0f);
    }

    
    
    private void TryFire()
    {
        // 没有按住鼠标左键。
        if (!isFiring)
        {
            return;
        }

        // 当前没有成功计算出瞄准点。
        if (!HasAimPoint)
        {
            return;
        }

        // 没有配置攻击组件。
        if (playerAttack == null)
        {
            Debug.LogWarning(
                "GrayboxPlayerController 没有设置 GamePlayerAttack。",
                this
            );

            return;
        }

        playerAttack.TryAttack(AimPoint);
    }

    private void TryStartRoll()
    {
        if (animator == null)
        {
            return;
        }

        if (Time.time < nextRollAllowedTime)
        {
            return;
        }

        Vector3 rollDirection = moveInputDirection;

        /*
         * 有 WASD 输入：
         * 朝玩家输入的移动方向翻滚。
         *
         * 没有 WASD 输入：
         * 朝鼠标方向翻滚。
         */
        if (rollDirection.magnitude < rollInputThreshold)
        {
            if (!TryGetMouseHorizontalDirection(out rollDirection))
            {
                rollDirection = hasLastAimDirection
                    ? lastAimDirection
                    : transform.forward;
            }
        }

        rollDirection.y = 0f;

        if (rollDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        rollDirection.Normalize();

        /*
         * 在播放动画之前先让角色朝向翻滚方向。
         * 动画自身只负责向角色正前方产生 Root Motion。
         */
        transform.rotation = Quaternion.LookRotation(
            rollDirection,
            Vector3.up
        );

        currentMoveDirection = Vector3.zero;
        rollVerticalVelocity = -2f;

        
        EnterAnimationDrivenWeaponState();
        
        isRolling = true;
        
        // 翻滚刚开始时还未进入无敌帧。
        // 真正的无敌时间由动画事件开启。
        isInvincible = false;
        
        nextRollAllowedTime = Time.time + rollCooldown;
        
        animator.ResetTrigger(rollTriggerHash);
        animator.SetTrigger(rollTriggerHash);
    }

    private void UpdateMovement()
    {
        if (lockstepMovementEnabled)
        {
            currentMoveDirection = lockstepMoveDirection;
            return;
        }

        currentMoveDirection = Vector3.MoveTowards(
            currentMoveDirection,
            moveInputDirection,
            acceleration * Time.deltaTime
        );

        characterController.SimpleMove(currentMoveDirection * walkSpeed *moveSpeedMultiplier);
            
       
    }

    /// <summary>
    /// 使用角色局部空间中的移动方向驱动二维 Blend Tree。
    ///
    /// MoveX：
    /// -1 = 向角色左侧移动
    ///  0 = 没有左右移动
    ///  1 = 向角色右侧移动
    ///
    /// MoveY：
    /// -1 = 向角色后方移动
    ///  0 = 没有前后移动
    ///  1 = 向角色前方移动
    /// </summary>
    private void UpdateLocomotionAnimator()
    {
        if (animator == null)
        {
            return;
        }

        /*
         * currentMoveDirection 是世界空间方向。
         * 角色一直朝向鼠标，所以不能直接把键盘 horizontal / vertical 交给动画。
         * 必须先把世界移动方向转换为角色自己的局部方向。
         */
        Vector3 localMoveDirection =
            transform.InverseTransformDirection(currentMoveDirection);

        float moveX = localMoveDirection.x;
        float moveY = localMoveDirection.z;

        // 接近静止时强制归零，避免 Blend Tree 在 Idle 附近轻微抖动。
        if (currentMoveDirection.sqrMagnitude < animationIdleThreshold)
        {
            moveX = 0f;
            moveY = 0f;
        }

        // 防止数值因为浮点误差略微超过二维 Blend Tree 的 -1～1 范围。
        Vector2 moveParameters = Vector2.ClampMagnitude(
            new Vector2(moveX, moveY),
            1f
        );

        animator.SetFloat(
            MoveXHash,
            moveParameters.x,
            animationDampTime,
            Time.deltaTime
        );

        animator.SetFloat(
            MoveYHash,
            moveParameters.y,
            animationDampTime,
            Time.deltaTime
        );
    }

    /// <summary>
    /// 在 Animator 与 Animation Rigging 计算前更新瞄准输入：
    /// 1. 使用经过 Muzzle 的水平面计算鼠标世界点。
    /// 2. 只让玩家根节点负责水平朝向。
    /// 3. 最后更新 AimTarget，让 Multi-Aim 和双手 IK 在本帧动画中求解。
    /// </summary>
    private void UpdateAimInputAndBodyRotation()
    {
        // 根据当前鼠标位置，计算玩家在 XZ 水平面上的目标瞄准方向。
        // 如果计算成功：
        // desiredAimDirection = 玩家应该朝向的水平世界方向。
        // hasCurrentAimDirection = true。
        hasCurrentAimDirection = TryGetMouseHorizontalDirection(out Vector3 stableAimDirection);
        

        // 如果当前没有获得有效的鼠标瞄准方向，
        // 就暂时不使用鼠标控制角色朝向。
        if (!hasCurrentAimDirection)
        {
            // 当前无法得到可靠的瞄准方向，
            // 因此也不能认为当前存在有效的最终 AimPoint。
            HasAimPoint = false;

            // 如果角色此时正在移动，
            // 那么让角色退化为“朝移动方向转身”。
            // 这样即使鼠标瞄准暂时失效，角色移动时也不会一直朝着旧方向。
            if (currentMoveDirection.sqrMagnitude > 0.01f)
            {
                RotateCharacterTowards(
                    currentMoveDirection,
                    turnSpeed * 60f
                );
            }

            // 没有有效瞄准方向时，
            // 本帧不再更新鼠标朝向，也不更新 Animation Rigging 的 AimTarget。
            return;
        }

        // 如果角色刚结束翻滚，
        // 不要瞬间把身体重新转回鼠标方向，
        // 而是使用较慢的旋转速度平滑恢复瞄准。
        if (isRecoveringAimAfterRoll)
        {
            // RotateCharacterTowardsSmooth 返回：
            // true  = 已经基本转到目标方向
            // false = 还没有完全转到目标方向
            //
            // 所以前面加 !：
            // 还没转完时 isRecoveringAimAfterRoll 保持 true，
            // 转完以后自动变回 false。
            isRecoveringAimAfterRoll = !RotateCharacterTowardsSmooth(
                stableAimDirection,
                postRollAimTurnSpeed
            );
        }
        else
        {
            // 正常状态下，让玩家根节点持续朝鼠标对应的水平瞄准方向旋转。
            RotateCharacterTowards(
                stableAimDirection,
                aimTurnSpeed
            );
        }

        // 玩家根节点的旋转会影响其所有子物体的世界坐标。
        // 因此必须先完成角色自身旋转，
        // 再更新 Animation Rigging 使用的 AimTarget 世界坐标。
        //
        // AimTarget 更新后，
        // 后续 Rig 才能基于新的目标位置计算枪械和双臂 IK。
        // 身体旋转完成后，让枪管方向直接对齐同一个稳定瞄准方向。
        // 这里不能通过 AimTarget - Muzzle 反算方向，否则旋转枪械带动
        // Muzzle 移动后会形成闭环反馈，在近距离产生来回抖动。
        UpdateWeaponHorizontalAimRotation(stableAimDirection);

        // 枪械旋转完成以后，再读取最终 Muzzle 位置放置 AimTarget。
        UpdateRigAimTarget(stableAimDirection);
        
        // 枪的位置和旋转确定以后，
        // 再把左右 Grip 的最终 Transform 同步给左右手 IK Target。
        SyncHandIKTargetsToWeaponGrips();
    }

    /// <summary>
    /// 当没有配置枪械方向参考点时，平滑旋转角色自身 forward。
    /// </summary>
    private bool RotateCharacterTowardsSmooth(
        Vector3 direction,
        float rotationSpeed)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return true;
        }

        Quaternion targetRotation = Quaternion.LookRotation(
            direction.normalized,
            Vector3.up
        );

        float remainingAngle = Quaternion.Angle(
            transform.rotation,
            targetRotation
        );

        if (remainingAngle <= postRollAimFinishAngle)
        {
            transform.rotation = targetRotation;
            return true;
        }

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );

        return false;
    }


    /// <summary>
    /// 使用muzzle点建立平面，用来与摄像机到鼠标的射线求交
    /// </summary>
    /// <returns></returns>
    private Plane getWeaponAimPlane()
    {
        Plane weaponAimPlane = new Plane(Vector3.up, muzzle.position);
        return weaponAimPlane;
    }

    /// 根据当前鼠标屏幕位置，计算角色在 XZ 水平面上的稳定瞄准方向。
    ///
    /// 主要流程：
    /// 1. 从相机经过鼠标位置发出射线。
    /// 2. 与玩家上方固定高度的水平面求交，得到 MouseAimWorldPoint。
    /// 3. 计算玩家根节点指向 MouseAimWorldPoint 的水平向量。
    /// 4. 鼠标进入角色中心死区时，不更新方向，继续保持 lastAimDirection。
    /// 5. 最终返回归一化后的水平瞄准方向。
    ///
    /// 这个函数只负责“角色身体应该朝哪个水平方向”，
    /// 不直接负责枪械最终 AimTarget 的位置。
    /// </summary>
    private bool TryGetMouseHorizontalDirection(out Vector3 direction)
    {
        
        // 如果当前正在等待玩家主动移动鼠标，
        // 就暂时不使用点击 UI 按钮时留下的鼠标位置进行瞄准。
        if (waitForAimMouseMovement)
        {
            // 计算鼠标当前位置与关闭 UI 时记录位置之间的偏移量。
            Vector3 mouseOffset = Input.mousePosition - aimResumeMousePosition;

            // sqrMagnitude 是偏移距离的平方。
            // 小于 4 表示鼠标移动距离不足 2 像素，
            // 这种情况视为鼠标仍停留在按钮附近。
            if (mouseOffset.sqrMagnitude < 4f)
            {
                // 鼠标仍停在 UI 按钮位置时，不读取按钮位置，
                // 继续使用暂停前最后一次有效的瞄准方向。
                direction = hasLastAimDirection
                    ? lastAimDirection
                    : transform.forward;

                return true;
            }

            // 鼠标已经由玩家主动移动，
            // 解除等待状态，从本帧开始恢复正常鼠标瞄准。
            waitForAimMouseMovement = false;
        }
        
        // 默认先返回一个零向量。
        // 只有后续成功得到有效瞄准方向时，才会真正写入 direction。
        direction = Vector3.zero;

        // 没有用于瞄准的相机时，无法把鼠标屏幕位置转换成世界方向。
        if (aimCamera == null)
        {
            HasMouseAimWorldPoint = false;
            return false;
        }

        // 鼠标离开 GameView 后，不再继续使用之前保存的 MouseAimWorldPoint。
        // 但是角色朝向不会立刻失效，而是继续保持最后一次稳定的瞄准方向。
        if (!IsPointerInsideGameView())
        {
            HasMouseAimWorldPoint = false;
            direction = lastAimDirection;
            return hasLastAimDirection;
        }

        // 从瞄准相机经过当前鼠标屏幕位置发出一条世界空间射线。
        Ray mouseRay = aimCamera.ScreenPointToRay(Input.mousePosition);
        
        Plane aimDirectionPlane = getWeaponAimPlane();

        // 尝试计算鼠标射线与角色瞄准水平面的交点。
        if (aimDirectionPlane.Raycast(mouseRay, out float enter))
        {
            // 得到鼠标在世界空间中的水平瞄准点。
            MouseAimWorldPoint = mouseRay.GetPoint(enter);
            HasMouseAimWorldPoint = true;

            // 计算玩家根节点指向鼠标世界点的方向。
            // 角色身体只允许水平旋转，所以清除 Y 轴高度差。
            Vector3 mouseOffsetFromCharacter = MouseAimWorldPoint - transform.position;
            mouseOffsetFromCharacter.y = 0f;

            // 基础死区半径。
            // 鼠标进入这个范围后，不再根据鼠标位置更新角色朝向。
            float deadZoneRadius = Mathf.Max(0f, mouseAimDeadZoneRadius);

            // 离开死区时额外增加一个滞回距离。
            // 这样鼠标在死区边缘轻微抖动时，不会反复切换“进入 / 离开死区”状态。
            float deadZoneExitRadius =
                deadZoneRadius + Mathf.Max(0f, mouseAimDeadZoneHysteresis);

            // 如果当前已经处于死区中，
            // 就使用更大的退出半径；
            // 如果当前还在死区外，则使用正常进入半径。
            float activeDeadZoneRadius = isMouseAimInsideDeadZone
                ? deadZoneExitRadius
                : deadZoneRadius;

            // 判断鼠标是否进入当前生效的死区范围。
            if (mouseOffsetFromCharacter.sqrMagnitude <=activeDeadZoneRadius * activeDeadZoneRadius)
            {
                // 标记已经进入死区。
                isMouseAimInsideDeadZone = true;

                // 死区内不根据鼠标当前位置重新计算方向，
                // 而是保持最后一次稳定的瞄准方向，
                // 防止鼠标靠近角色中心时角色突然快速翻转。
                direction = lastAimDirection;

                // 如果之前已经存在有效瞄准方向，则继续返回成功。
                return hasLastAimDirection;
            }

            // 鼠标已经位于死区之外，恢复正常瞄准更新。
            isMouseAimInsideDeadZone = false;

            // 当前角色水平瞄准方向就是：
            // Player → MouseAimWorldPoint。
            direction = mouseOffsetFromCharacter;
        }
        else
        {
            // 极端情况下，相机射线可能与水平面平行，
            // 导致无法获得实际交点。
            isMouseAimInsideDeadZone = false;
            HasMouseAimWorldPoint = false;

            // 此时退化为直接使用相机射线方向的水平投影。
            direction = mouseRay.direction;
            direction.y = 0f;
        }

        // 如果最终得到的水平向量仍然接近零，
        // 就继续使用最后一次稳定的瞄准方向。
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = lastAimDirection;
            return hasLastAimDirection;
        }

        // 归一化，只保留方向，不保留距离大小。
        direction.Normalize();

        // 保存本次有效方向。
        // 后续鼠标进入死区、离开 GameView 或计算失败时，
        // 都可以继续使用这个方向保持角色朝向稳定。
        lastAimDirection = direction;
        hasLastAimDirection = true;

        // 成功获得有效的水平瞄准方向。
        return true;
    }

    private void ResolveAimRigReferences()
    {
        Transform searchRoot = animator != null
            ? animator.transform
            : transform;

        if (aimRig == null)
        {
            Rig[] rigs = searchRoot.GetComponentsInChildren<Rig>(true);

            for (int i = 0; i < rigs.Length; i++)
            {
                if (rigs[i].name == "AimRig")
                {
                    aimRig = rigs[i];
                    break;
                }
            }

            if (aimRig == null && rigs.Length > 0)
            {
                aimRig = rigs[0];
            }
        }

        if (aimRigTarget == null)
        {
            Transform targetSearchRoot = aimRig != null
                ? aimRig.transform
                : searchRoot;

            aimRigTarget = FindDescendantByName(
                targetSearchRoot,
                "AimTarget"
            );
        }
    }

    /// <summary>
    /// 翻滚、受击和死亡动画期间关闭双手 IK，并把枪挂到右手骨骼下。
    /// SetParent(worldPositionStays: true) 会保留切换瞬间的世界姿态，
    /// 随后枪作为右手子节点自然跟随动画运动。
    /// </summary>
    private void EnterAnimationDrivenWeaponState()
    {
        if (aimRig != null)
        {
            aimRig.weight = 0f;
        }

        if (weaponSocket == null || rightHandBone == null)
        {
            return;
        }

        if (weaponSocket.parent != rightHandBone)
        {
            weaponSocket.SetParent(rightHandBone, true);
        }
    }

    /// <summary>
    /// 死亡流程在禁用玩家控制器前调用。
    /// 死亡动画期间保持 IK 关闭，并让枪持续跟随右手。
    /// </summary>
    public void EnterDeathWeaponState()
    {
        isFiring = false;
        waitForPrimaryFireRelease = true;
        EnterAnimationDrivenWeaponState();
    }
    
    /// <summary>
    /// 将左右手的 IK Target 同步到枪上的左右手 Grip。
    /// 枪移动或旋转以后，确保双手 IK 继续跟随正确的握枪位置。
    /// </summary>
    private void SyncHandIKTargetsToWeaponGrips()
    {
        // 右手 Target 跟随枪上的 RightHandGrip。
        if (rightHandTarget != null && rightHandGrip != null)
        {
            rightHandTarget.SetPositionAndRotation(rightHandGrip.position, rightHandGrip.rotation);
        }

        // 左手 Target 跟随枪上的 LeftHandGrip。
        if (leftHandTarget != null && leftHandGrip != null)
        {
            leftHandTarget.SetPositionAndRotation(leftHandGrip.position, leftHandGrip.rotation);
        }
    }
    
    private static Transform FindDescendantByName(
        Transform parent,
        string objectName)
    {
        if (parent == null)
        {
            return null;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);

            if (child.name == objectName)
            {
                return child;
            }

            Transform result = FindDescendantByName(child, objectName);

            if (result != null)
            {
                return result;
            }
        }

        return null;
    }

    private void UpdateAimRigWeight(bool shouldAim)
    {
        if (aimRig == null)
        {
            ResolveAimRigReferences();
        }

        if (aimRig == null)
        {
            return;
        }

        float targetWeight = shouldAim ? 1f : 0f;

        if (aimRigBlendSpeed <= 0f)
        {
            aimRig.weight = targetWeight;
            return;
        }

        aimRig.weight = Mathf.MoveTowards(
            aimRig.weight,
            targetWeight,
            aimRigBlendSpeed * Time.deltaTime
        );
    }

    /// <summary>
    /// 根据 AimTarget 修正 WeaponAimPivot 的水平旋转。
    /// 只绕世界 Y 轴旋转枪械，不产生上下俯仰和侧倾。
    /// </summary>
    private void UpdateWeaponHorizontalAimRotation(Vector3 stableAimDirection)
    {
        // 缺少枪械瞄准所需要的任意引用时，不进行旋转。
        if (weaponAimPivot == null || weaponAimStart == null || weaponAimEnd == null)
        {
            return;
        }

        // WeaponAimStart → WeaponAimEnd 表示枪当前真实的枪管方向。
        Vector3 currentWeaponDirection = weaponAimEnd.position - weaponAimStart.position;

        // 枪和身体使用同一个稳定瞄准方向。
        // 这个方向不依赖 Muzzle 或 AimTarget 的当前位置。
        currentWeaponDirection.y = 0f;
        stableAimDirection.y = 0f;

        // 防止方向长度过小时进行无意义的旋转计算。
        if (currentWeaponDirection.sqrMagnitude < 0.0001f || stableAimDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        currentWeaponDirection.Normalize();
        stableAimDirection.Normalize();

        // 计算枪当前方向到目标方向之间绕世界 Y 轴的水平角度差。
        float yawDelta = Vector3.SignedAngle(currentWeaponDirection, stableAimDirection, Vector3.up);

        // 只把这个水平角度差施加给 WeaponAimPivot。
        weaponAimPivot.rotation = Quaternion.AngleAxis(yawDelta, Vector3.up) * weaponAimPivot.rotation;
    }
    
    /// <summary>
    /// 根据稳定瞄准方向更新枪械 AimTarget。
    /// 相机射线仍与经过 Muzzle 的水平面求交，但交点只提供目标距离；
    /// AimTarget 的方向始终使用身体和枪共同消费的稳定瞄准方向。
    /// </summary>
    private void UpdateRigAimTarget(Vector3 desiredAimDirection)
    {
        hasWeaponAimWorldPoint = false;

        // AimTarget 丢失时尝试重新查找。
        if (aimRigTarget == null)
        {
            ResolveAimRigReferences();
        }

        // 缺少必要引用时无法计算枪械瞄准目标。
        if (aimRigTarget == null || muzzle == null || aimCamera == null)
        {
            return;
        }

        // 枪械只允许水平瞄准，因此备用方向只保留 XZ 分量。
        desiredAimDirection.y = 0f;

        if (desiredAimDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        desiredAimDirection.Normalize();

        float minimumDistance = Mathf.Max(0.1f, weaponAimTargetMinDistance);

        // 没有有效鼠标世界点时，退回最后稳定的水平瞄准方向。
        if (!HasMouseAimWorldPoint)
        {
            SetWeaponAimWorldPoint(muzzle.position + desiredAimDirection * minimumDistance);
            return;
        }

        // 从游戏相机位置指向实际世界准心。
        // 这条射线一定经过 CrosshairRoot，因为 CrosshairRoot.position = MouseAimWorldPoint。
        Vector3 crosshairRayDirection = MouseAimWorldPoint - aimCamera.transform.position;

        if (crosshairRayDirection.sqrMagnitude < 0.0001f)
        {
            SetWeaponAimWorldPoint(muzzle.position + desiredAimDirection * minimumDistance);
            return;
        }

        Ray crosshairRay = new Ray(aimCamera.transform.position, crosshairRayDirection.normalized);

        // 建立经过 Muzzle 的水平面。
        // AimTarget 最终与 Muzzle 保持相同高度。
        Plane weaponAimPlane = getWeaponAimPlane();

        // 求“相机 → 实际准心”的射线与枪口水平面的交点。
        if (!weaponAimPlane.Raycast(crosshairRay, out float enter))
        {
            SetWeaponAimWorldPoint(muzzle.position + desiredAimDirection * minimumDistance);
            return;
        }

        Vector3 targetPoint = crosshairRay.GetPoint(enter);

        // 计算原始目标点到枪口的水平距离。
        Vector3 targetOffset = targetPoint - muzzle.position;
        targetOffset.y = 0f;
        float targetDistance = targetOffset.magnitude;

        /*
         * 相机射线仍然与经过 Muzzle 的水平面求交，确保瞄准系统工作在
         * 枪口高度的水平面上；但交点只提供目标距离，不再反向决定枪的朝向。
         *
         * AimTarget 始终放在最终 Muzzle 沿 stableAimDirection 的前方，
         * 因而身体、枪、AimTarget、射击射线和瞄准线共用唯一方向。
         */
        targetPoint =
            muzzle.position +
            desiredAimDirection * Mathf.Max(targetDistance, minimumDistance);

        // 同时更新 AimTarget 和后续射击/瞄准线使用的目标点。
        SetWeaponAimWorldPoint(targetPoint);
    }
    
    public bool TryGetCrosshairWorldPoint(out Vector3 crosshairWorldPoint)
    {
        crosshairWorldPoint = Vector3.zero;

        // 死区内身体和枪保持最后稳定方向，但准心仍然显示原始鼠标位置。
        // 因此这里允许准心与实际射击方向暂时分离。
        if (isMouseAimInsideDeadZone)
        {
            if (!HasMouseAimWorldPoint)
            {
                return false;
            }

            crosshairWorldPoint = MouseAimWorldPoint;
            return true;
        }

        // 死区外，准心重新表示枪械最终瞄准目标。
        if (!hasWeaponAimWorldPoint)
        {
            return false;
        }

        crosshairWorldPoint = weaponAimWorldPoint;
        return true;
    }

    /// <summary>
    /// 同时保存枪械校准、物理射线和瞄准线共用的世界目标点。
    /// </summary>
    private void SetWeaponAimWorldPoint(Vector3 targetPoint)
    {
        aimRigTarget.position = targetPoint;
        weaponAimWorldPoint = targetPoint;
        hasWeaponAimWorldPoint = true;
    }

    private void UpdateAimPointFromWeaponRay(Vector3 fallbackDirection)
    {
        Transform aimOriginTransform = GetAimOriginTransform();

        if (aimOriginTransform == null)
        {
            HasAimPoint = false;
            return;
        }

        Vector3 fireOrigin = aimOriginTransform.position;
        Vector3 rayDirection = fallbackDirection;
        rayDirection.y = 0f;

        /*
         * 正常瞄准时必须使用 UpdateRigAimTarget 算出的同一个目标点。
         * 不能在这里重新读取枪械模型方向，否则模型装配角、Pivot 偏移
         * 或动画求解误差会让红线和子弹稳定地偏离屏幕准心。
         */
        if (hasWeaponAimWorldPoint)
        {
            rayDirection = weaponAimWorldPoint - fireOrigin;
            rayDirection.y = 0f;
        }
        // 只有无法获得准心目标点时，才退回枪管参考方向。
        else if (weaponAimStart != null && weaponAimEnd != null)
        {
            Vector3 currentWeaponDirection =
                weaponAimEnd.position -
                weaponAimStart.position;

            if (currentWeaponDirection.sqrMagnitude >= 0.0001f)
            {
                rayDirection = currentWeaponDirection;
                rayDirection.y = 0f;
            }
        }

        if (rayDirection.sqrMagnitude < 0.0001f)
        {
            HasAimPoint = false;
            return;
        }

        rayDirection.Normalize();

        // 稍微向前偏移射线起点，降低击中自身武器或角色碰撞体的概率。
        Vector3 rayOrigin =
            fireOrigin +
            rayDirection * aimRayStartOffset;

        int hitCount = Physics.RaycastNonAlloc(
            rayOrigin,
            rayDirection,
            aimHits,
            aimRayDistance,
            aimHitMask,
            QueryTriggerInteraction.Ignore
        );

        bool foundHit = false;
        float nearestDistance = float.PositiveInfinity;
        RaycastHit nearestHit = default;

        // RaycastNonAlloc 的结果不保证按距离排序，所以手动寻找最近的有效碰撞。
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = aimHits[i];

            if (hit.collider == null)
            {
                continue;
            }

            // 忽略 Player 根节点及其所有子物体上的碰撞体。
            if (hit.collider.transform.root == transform.root)
            {
                continue;
            }

            if (hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = hit.distance;
            nearestHit = hit;
            foundHit = true;
        }

        AimPoint = foundHit
            ? nearestHit.point
            : fireOrigin + rayDirection * aimRayDistance;

        HasAimPoint = true;

        // Scene 视图调试线：红色代表命中物体，黄色代表指向远点。
        Debug.DrawLine(
            fireOrigin,
            AimPoint,
            foundHit ? Color.red : Color.yellow
        );
    }

    /// <summary>
    /// 枪方向参考点缺失时的备用旋转方式。
    /// </summary>
    private void RotateCharacterTowards(Vector3 direction, float rotationSpeed)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(
            direction.normalized,
            Vector3.up
        );

        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    /// <summary>
    /// 优先使用 muzzle 作为射线起点；未配置时退回 weaponAimEnd。
    /// </summary>
    private Transform GetAimOriginTransform()
    {
        if (muzzle != null)
        {
            return muzzle;
        }

        return weaponAimEnd;
    }

    private void CreateAimLine()
    {
        if (aimLine != null)
        {
            return;
        }

        GameObject lineObject = new GameObject("Aim Line");
        lineObject.transform.SetParent(transform, false);

        aimLine = lineObject.AddComponent<LineRenderer>();
        aimLine.useWorldSpace = true;
        aimLine.positionCount = 2;
        aimLine.alignment = LineAlignment.View;
        aimLine.numCapVertices = 4;
        aimLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        aimLine.receiveShadows = false;
        aimLine.sortingOrder = 10;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            Debug.LogWarning("未找到用于 Aim Line 的 Shader。", this);
            return;
        }

        aimLineMaterial = new Material(shader);
        aimLine.material = aimLineMaterial;
        SetAimLineColor();
    }

    private void UpdateAimLine()
    {
        if (aimLine == null)
        {
            CreateAimLine();
        }

        if (aimLine == null)
        {
            return;
        }

        bool shouldShow =
            !isRolling &&
            showAimLine &&
            muzzle != null &&
            HasAimPoint;

        aimLine.enabled = shouldShow;

        if (!shouldShow)
        {
            return;
        }

        aimLine.widthMultiplier = aimLineWidth;

        // 起点永远跟随枪口。
        aimLine.SetPosition(0, muzzle.position);

        /*
         * 受击状态：
         * 不再让瞄准线指向 AimPoint，
         * 而是沿枪当前被动画带动后的真实方向延伸。
         */
        if (isHitStunned)
        {
            Vector3 weaponDirection = GetCurrentWeaponDirection();

            if (weaponDirection.sqrMagnitude < 0.0001f)
            {
                weaponDirection = muzzle.forward;
            }

            weaponDirection.Normalize();

            // 保持瞄准线原本大致的长度。
            float lineLength = Vector3.Distance(
                muzzle.position,
                AimPoint
            );

            aimLine.SetPosition(
                1,
                muzzle.position +
                weaponDirection * lineLength
            );

            return;
        }

        /*
         * 正常状态：
         * 仍然指向正常计算出来的 AimPoint。
         */
        aimLine.SetPosition(1, AimPoint);
    }
    
    private Vector3 GetCurrentWeaponDirection()
    {
        if (weaponAimStart != null &&weaponAimEnd != null)
        {
            Vector3 direction =weaponAimEnd.position -weaponAimStart.position;

            if (direction.sqrMagnitude >= 0.0001f)
            {
                return direction.normalized;
            }
        }

        if (muzzle != null)
        {
            return muzzle.forward;
        }

        return transform.forward;
    }

    public void ApplyRollRootMotion(Vector3 animatorDeltaPosition)
    {
        if (lockstepMovementEnabled || !isRolling || characterController == null)
        {
            return;
        }

        /*
         * 只采用动画的 XZ 位移。
         * Y 方向由 CharacterController 的重力控制，
         * 避免角色跟随翻滚动画上下弹跳。
         */
        Vector3 movementDelta = animatorDeltaPosition;
        movementDelta.y = 0f;

        if (characterController.isGrounded &&
            rollVerticalVelocity < 0f)
        {
            rollVerticalVelocity = -2f;
        }
        else
        {
            rollVerticalVelocity +=
                Physics.gravity.y * Time.deltaTime;
        }

        movementDelta.y =
            rollVerticalVelocity * Time.deltaTime;

        characterController.Move(movementDelta);
    }

    public void FinishRoll()
    {
        // 保险处理：无论 EndRollInvincibility 动画事件有没有正常触发，
        // 翻滚结束时都必须取消无敌。
        isInvincible = false;

        isRolling = false;
        currentMoveDirection = Vector3.zero;
        rollVerticalVelocity = -2f;

        RestoreWeaponNormalPose();
        
        // 翻滚结束后，不瞬间对准鼠标，而是进入平滑恢复阶段。
        isRecoveringAimAfterRoll = true;
    }
    
    private void OnDisable()
    {
        isInvincible = false;
        hasCurrentAimDirection = false;

        // 玩家死亡或控制器被禁用后，让死亡/剧情动画不再受持枪 IK 约束。
        if (aimRig != null)
        {
            aimRig.weight = 0f;
        }
    }

    /// <summary>
    /// 由翻滚动画事件调用，开始无敌帧。
    /// </summary>
    public void BeginRollInvincibility()
    {
        if (!isRolling)
        {
            return;
        }

        isInvincible = true;
    }

    /// <summary>
    /// 由翻滚动画事件调用，结束无敌帧。
    /// </summary>
    public void EndRollInvincibility()
    {
        isInvincible = false;
    }
    
    private void SetAimLineColor()
    {
        if (aimLine == null || aimLineMaterial == null)
        {
            return;
        }

        aimLine.startColor = aimLineColor;
        aimLine.endColor = aimLineColor;
        aimLineMaterial.color = aimLineColor;

        if (aimLineMaterial.HasProperty("_BaseColor"))
        {
            aimLineMaterial.SetColor("_BaseColor", aimLineColor);
        }
    }

    private bool IsPointerInsideGameView()
    {
#if UNITY_EDITOR
        UnityEditor.EditorWindow window = UnityEditor.EditorWindow.mouseOverWindow;

        if (window == null || window.GetType().Name != "GameView")
        {
            return false;
        }
#endif

        Vector3 mousePosition = Input.mousePosition;

        return mousePosition.x >= 0f &&
               mousePosition.x <= Screen.width &&
               mousePosition.y >= 0f &&
               mousePosition.y <= Screen.height;
    }

    private void OnDrawGizmosSelected()
    {
        if (weaponAimStart != null && weaponAimEnd != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(
                weaponAimStart.position,
                weaponAimEnd.position
            );
        }

        if (!HasAimPoint)
        {
            return;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawSphere(AimPoint, 0.12f);

        Transform aimOriginTransform = GetAimOriginTransform();

        if (aimOriginTransform != null)
        {
            Gizmos.DrawLine(
                aimOriginTransform.position,
                AimPoint
            );
        }
    }

    private void OnDestroy()
    {
        if (aimLineMaterial != null)
        {
            Destroy(aimLineMaterial);
        }
    }
}
