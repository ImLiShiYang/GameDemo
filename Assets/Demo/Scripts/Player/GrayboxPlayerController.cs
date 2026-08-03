using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 第三人称俯视角角色控制器：
/// 1. WASD 相对相机方向移动。
/// 2. 使用 MoveX / MoveY 驱动包含 Idle、前、后、左、右的二维 Blend Tree。
/// 3. 鼠标只提供 XZ 平面上的瞄准方向。
/// 4. 无论是否按下鼠标左键，都根据枪当前的真实方向旋转整个角色。
/// 5. 从枪口沿瞄准方向发射射线：命中物体时 AimPoint 为命中点，
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

    [Header("Weapon Aim")]
    [Tooltip("枪口位置，同时作为物理射线和瞄准线的起点。")]
    [SerializeField] private Transform muzzle;

    [Tooltip("枪身后方参考点。")]
    [SerializeField] private Transform weaponAimStart;

    [Tooltip("枪口前方参考点。Start 指向 End 的方向必须沿着子弹发射方向。")]
    [SerializeField] private Transform weaponAimEnd;

    [Header("Aim Line")]
    [SerializeField] private bool showAimLine = true;
    [SerializeField] private bool onlyShowAimLineWhileFiring = true;
    [SerializeField] private Color aimLineColor = new Color(0f, 1f, 0.8f, 1f);
    [SerializeField] private float aimLineWidth = 0.04f;
    [SerializeField] private float aimLineEndHeight = 0.03f;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Animator animator;
    
    [Header("Dodge Roll")]
    [SerializeField] private KeyCode rollKey = KeyCode.LeftShift;

    [Tooltip("两次翻滚之间的最短间隔。")]
    [SerializeField] private float rollCooldown = 0.35f;

    [Tooltip("小于这个输入强度时，认为角色没有移动输入。")]
    [SerializeField] private float rollInputThreshold = 0.05f;

    [Tooltip("Animator 中翻滚 Trigger 参数的名字。")]
    [SerializeField] private string rollTriggerName = "Roll";

    private Vector3 moveInputDirection;
    private bool isRolling;
    private float nextRollAllowedTime;
    private float rollVerticalVelocity;
    private int rollTriggerHash;

    public bool IsRolling => isRolling;

    private CharacterController characterController;
    private Vector3 currentMoveDirection;

    // 缓存 RaycastNonAlloc 的结果，避免每帧创建新数组。
    private readonly RaycastHit[] aimHits = new RaycastHit[16];

    private LineRenderer aimLine;
    private Material aimLineMaterial;

    private bool isFiring;
    private Vector3 lastAimDirection;
    private bool hasLastAimDirection;

    /// <summary>
    /// 枪口射线最终指向的世界坐标。
    /// 命中物体时是碰撞点；未命中时是枪口前方远点。
    /// </summary>
    public Vector3 AimPoint { get; private set; }

    /// <summary>
    /// 当前是否拥有有效瞄准点。
    /// </summary>
    public bool HasAimPoint { get; private set; }

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

        if (cameraTransform == null && aimCamera != null)
        {
            cameraTransform = aimCamera.transform;
        }
        
        rollTriggerHash = Animator.StringToHash(rollTriggerName);

        CreateAimLine();
    }

    private void Update()
    {
        // 每帧都读取移动输入，确保按下 Shift 的这一帧能获得正确方向。
        UpdateMoveInput();

        // 翻滚时不允许开枪。
        isFiring = !isRolling && Input.GetMouseButton(0);

        if (!isRolling && Input.GetKeyDown(rollKey))
        {
            TryStartRoll();
        }

        if (isRolling)
        {
            // 翻滚期间暂停普通 CharacterController.SimpleMove。
            currentMoveDirection = Vector3.zero;
            ResetLocomotionAnimator();
            return;
        }

        UpdateMovement();
        UpdateLocomotionAnimator();
    }
    
    private void UpdateMoveInput()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

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

        moveInputDirection =
            forward * vertical +
            right * horizontal;

        moveInputDirection = Vector3.ClampMagnitude(
            moveInputDirection,
            1f
        );
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

    private void LateUpdate()
    {
        if (!isRolling)
        {
            UpdateAim();
        }

        UpdateAimLine();
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
            Transform aimOrigin = GetAimOriginTransform();

            Vector3 origin = aimOrigin != null
                ? aimOrigin.position
                : transform.position;

            if (!TryGetMouseHorizontalDirection(origin, out rollDirection))
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

        isRolling = true;
        nextRollAllowedTime = Time.time + rollCooldown;

        animator.ResetTrigger(rollTriggerHash);
        animator.SetTrigger(rollTriggerHash);
    }

    private void UpdateMovement()
    {
        currentMoveDirection = Vector3.MoveTowards(
            currentMoveDirection,
            moveInputDirection,
            acceleration * Time.deltaTime
        );

        characterController.SimpleMove(
            currentMoveDirection * walkSpeed
        );
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
    /// 统一瞄准流程：
    /// 1. 鼠标只计算 XZ 水平方向。
    /// 2. 根据枪当前真实方向旋转整个角色。
    /// 3. 从旋转后的枪口发射射线，得到最终 AimPoint。
    /// </summary>
    private void UpdateAim()
    {
        Transform aimOriginTransform = GetAimOriginTransform();

        if (aimOriginTransform == null || aimCamera == null)
        {
            HasAimPoint = false;
            return;
        }

        // 鼠标只提供世界空间中的水平方向，不直接提供地面目标点。
        if (!TryGetMouseHorizontalDirection(
                aimOriginTransform.position,
                out Vector3 desiredAimDirection))
        {
            HasAimPoint = false;

            // 完全没有鼠标方向时，退回朝当前移动方向旋转。
            if (currentMoveDirection.sqrMagnitude > 0.01f)
            {
                RotateCharacterTowards(
                    currentMoveDirection,
                    turnSpeed * 60f
                );
            }

            return;
        }

        if (weaponAimStart != null && weaponAimEnd != null)
        {
            // 使用枪上两个点形成的真实方向，校正整个角色的 Y 轴旋转。
            AlignWeaponToDirection(desiredAimDirection);
        }
        else
        {
            // 枪方向参考点未配置时，退回使用角色自身 forward。
            RotateCharacterTowards(desiredAimDirection, aimTurnSpeed);
        }

        // 角色旋转完成后，再从最新枪口位置发射射线。
        UpdateAimPointFromWeaponRay(desiredAimDirection);
    }

    /// <summary>
    /// 根据鼠标屏幕坐标计算 XZ 平面上的世界方向。
    /// 鼠标不直接决定目标高度，只决定水平方向。
    /// </summary>
    private bool TryGetMouseHorizontalDirection(
        Vector3 origin,
        out Vector3 direction)
    {
        direction = Vector3.zero;

        if (aimCamera == null)
        {
            return false;
        }

        // 鼠标离开 GameView 后继续沿用最后一次有效方向。
        if (!IsPointerInsideGameView())
        {
            if (!hasLastAimDirection)
            {
                return false;
            }

            direction = lastAimDirection;
            return true;
        }

        Ray mouseRay = aimCamera.ScreenPointToRay(Input.mousePosition);

        // 创建一个经过枪口高度的水平面。
        // 鼠标射线与这个平面的交点只用于计算 XZ 方向。
        Plane aimDirectionPlane = new Plane(Vector3.up, origin);

        if (aimDirectionPlane.Raycast(mouseRay, out float enter))
        {
            Vector3 mousePointAtWeaponHeight = mouseRay.GetPoint(enter);
            direction = mousePointAtWeaponHeight - origin;
            direction.y = 0f;
        }
        else
        {
            // 极端情况下相机射线与水平面平行，使用相机射线的水平投影。
            direction = mouseRay.direction;
            direction.y = 0f;
        }

        if (direction.sqrMagnitude < 0.0001f)
        {
            if (!hasLastAimDirection)
            {
                return false;
            }

            direction = lastAimDirection;
            return true;
        }

        direction.Normalize();

        lastAimDirection = direction;
        hasLastAimDirection = true;
        return true;
    }

    /// <summary>
    /// 比较“枪当前真实方向”和“鼠标目标方向”的水平夹角，
    /// 再把这个角度差施加给整个角色根节点。
    /// </summary>
    private void AlignWeaponToDirection(Vector3 desiredAimDirection)
    {
        desiredAimDirection.y = 0f;

        if (desiredAimDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        desiredAimDirection.Normalize();

        // 旋转整个角色后，枪上参考点的世界位置也会变化。
        // 重算两次可以减少由旋转中心偏移产生的残余误差。
        for (int i = 0; i < 2; i++)
        {
            Vector3 currentWeaponDirection =
                weaponAimEnd.position -
                weaponAimStart.position;

            currentWeaponDirection.y = 0f;

            if (currentWeaponDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            currentWeaponDirection.Normalize();

            float angleDifference = Vector3.SignedAngle(
                currentWeaponDirection,
                desiredAimDirection,
                Vector3.up
            );

            if (Mathf.Abs(angleDifference) < 0.01f)
            {
                break;
            }

            transform.rotation =
                Quaternion.AngleAxis(angleDifference, Vector3.up) *
                transform.rotation;
        }
    }

    /// <summary>
    /// 从旋转后的枪口沿枪的水平方向发射射线：
    /// 命中物体时 AimPoint 为命中点；未命中时 AimPoint 为远点。
    /// </summary>
    private void UpdateAimPointFromWeaponRay(Vector3 fallbackDirection)
    {
        Transform aimOriginTransform = GetAimOriginTransform();

        if (aimOriginTransform == null)
        {
            HasAimPoint = false;
            return;
        }

        Vector3 rayDirection = fallbackDirection;
        rayDirection.y = 0f;

        // 枪方向参考点有效时，让物理射线严格沿枪当前真实方向发射。
        if (weaponAimStart != null && weaponAimEnd != null)
        {
            Vector3 currentWeaponDirection =
                weaponAimEnd.position -
                weaponAimStart.position;

            currentWeaponDirection.y = 0f;

            if (currentWeaponDirection.sqrMagnitude >= 0.0001f)
            {
                rayDirection = currentWeaponDirection;
            }
        }

        if (rayDirection.sqrMagnitude < 0.0001f)
        {
            HasAimPoint = false;
            return;
        }

        rayDirection.Normalize();

        Vector3 fireOrigin = aimOriginTransform.position;

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
            (!onlyShowAimLineWhileFiring || isFiring) &&
            muzzle != null &&
            HasAimPoint;

        aimLine.enabled = shouldShow;

        if (!shouldShow)
        {
            return;
        }

        aimLine.widthMultiplier = aimLineWidth;
        aimLine.SetPosition(0, muzzle.position);
        aimLine.SetPosition(1, AimPoint + Vector3.up * aimLineEndHeight);
    }

    public void ApplyRollRootMotion(Vector3 animatorDeltaPosition)
    {
        if (!isRolling || characterController == null)
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
        isRolling = false;
        currentMoveDirection = Vector3.zero;
        rollVerticalVelocity = -2f;
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
