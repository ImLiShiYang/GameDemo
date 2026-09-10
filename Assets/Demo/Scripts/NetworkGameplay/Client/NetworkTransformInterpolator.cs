using UnityEngine;

[DefaultExecutionOrder(-100)]
public sealed class NetworkTransformInterpolator : MonoBehaviour
{
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int AttackHash = Animator.StringToHash("Attack");

    [SerializeField, Min(1f)] private float interpolationRate = 15f;
    [SerializeField, Min(0.1f)] private float snapDistance = 5f;
    [SerializeField, Range(0.05f, 0.5f)] private float snapshotDelay = 0.2f;
    [Tooltip("缓冲耗尽后最多猜测多少秒；0 为关闭外推。仅影响远程显示，不影响逻辑身体。")]
    [SerializeField, Range(0f, 0.25f)] private float maxExtrapolationTime = 0.15f;
    [Tooltip("外推结束后的误差衰减速度；数值越大，恢复到真实时间轴越快。")]
    [SerializeField, Min(1f)] private float extrapolationCorrectionRate = 20f;
    private readonly SnapshotInterpolationBuffer snapshots = new SnapshotInterpolationBuffer();
    private readonly RaycastHit[] extrapolationHits = new RaycastHit[16];
    private bool snapshotPlayback;
    private bool latestDead;
    private bool pendingExtrapolationCorrection;
    private Vector3 correctionOffset;
    private Quaternion correctionRotation = Quaternion.identity;
    public int BufferedSnapshotCount => snapshots.Count;
    public double PlaybackServerTime => snapshots.PlaybackTime;
    public bool SnapshotBufferStarved => snapshots.Starved;
    public bool IsExtrapolating => snapshots.IsExtrapolating;
    public double ExtrapolatedSeconds => snapshots.ExtrapolatedSeconds;

    private Animator animator;
    private PlayerPresentationDriver presentationDriver;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool initialized;
    private bool localAuthorityView;
    private bool hasMoveX;
    private bool hasMoveY;
    private bool hasSpeed;
    private bool hasAttack;

    public void Initialize(bool isLocalPlayer)
    {
        localAuthorityView = isLocalPlayer;
        animator = GetComponentInChildren<Animator>();
        presentationDriver = GetComponent<PlayerPresentationDriver>();
        CacheAnimatorParameters();
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        initialized = false;
        snapshotPlayback = false;
        latestDead = false;
        ClearExtrapolationCorrection();
        snapshots.Clear();
    }

    public void ApplyState(PlayerNetworkState state, uint serverTick)
    {
        ReceiveSnapshot(new InterpolationSnapshot
        {
            Tick = serverTick, Position = state.Position, Rotation = Quaternion.Euler(0f, state.RotationY, 0f),
            MoveSpeed = state.MoveSpeed, IsPlayer = true, Action = state.Action, IsFiring = state.IsFiring, Dead = state.CurrentHealth <= 0f
        });
    }

    public void ApplyState(EntityNetworkState state, uint serverTick)
    {
        ReceiveSnapshot(new InterpolationSnapshot
        {
            Tick = serverTick, Position = state.Position, Rotation = Quaternion.Euler(0f, state.RotationY, 0f),
            MoveSpeed = new Vector2(state.Velocity.x, state.Velocity.z).magnitude,
            Velocity = state.Velocity,
            HasVelocity = true,
            Dead = state.CurrentHealth <= 0f
        });
    }

    public void ApplySpawn(EntitySpawnMessage message, uint packetServerTick)
    {
        snapshots.Clear();
        ReceiveSnapshot(new InterpolationSnapshot { Tick = packetServerTick, Position = message.Position, Rotation = message.Rotation });
    }

    private void ReceiveSnapshot(InterpolationSnapshot frame)
    {
        // 记住收包前是否已经进入“未知未来”。如果是，新快照到来后需要从旧显示位置平滑纠偏。
        bool wasExtrapolating = snapshots.IsExtrapolating;
        if (!snapshots.Push(frame, snapshotDelay, snapDistance, out bool reset))
            return;
        snapshotPlayback = true;
        initialized = true;
        latestDead = frame.Dead;
        // 首帧、传送、长中断和死亡不能拖着旧误差慢慢走，必须立即清除外推残留。
        if (reset || latestDead)
            ClearExtrapolationCorrection();
        else if (wasExtrapolating)
            pendingExtrapolationCorrection = true;
        if (reset)
            RenderSnapshot(frame);
        // 死亡优先于延迟播放，旧缓冲帧不能让角色重新播放翻滚/开火。
        if (frame.IsPlayer && latestDead)
            GetComponent<GrayboxPlayerController>()?.ApplyNetworkMotion(frame.Action, false, true);
    }

    private void RenderSnapshot(InterpolationSnapshot frame)
    {
        Vector3 displayPosition = frame.Position;
        // 外推只能穿过“时间”，不能穿过静态墙。碰撞代理和其他角色不参与这个显示查询，
        // 因为它们处于最新权威时间，而模型正在播放较早的时间轴，两种时间不能混用。
        if (snapshots.IsExtrapolating)
            displayPosition = ConstrainDisplayToWorld(snapshots.LatestPosition, displayPosition);

        if (pendingExtrapolationCorrection)
        {
            // 恢复包到来时，用当前模型与新时间轴采样结果的差作为视觉偏移。
            // 第一帧因此保持原画面位置，后续再指数衰减，不会突然“啪”地拉回服务器点。
            correctionOffset = transform.position - displayPosition;
            correctionRotation = transform.rotation * Quaternion.Inverse(frame.Rotation);
            // 误差大于传送阈值时不做柔和纠偏，避免拖着模型跨越大段地图。
            if (correctionOffset.magnitude > snapDistance)
                ClearExtrapolationCorrection();
            pendingExtrapolationCorrection = false;
        }

        Vector3 correctedPosition = displayPosition + correctionOffset;
        if (correctionOffset.sqrMagnitude > 0.000001f)
            correctedPosition = ConstrainDisplayToWorld(displayPosition, correctedPosition);
        Quaternion correctedRotation = correctionRotation * frame.Rotation;
        if (presentationDriver != null) presentationDriver.SetInterpolatedPose(correctedPosition, correctedRotation);
        else transform.SetPositionAndRotation(correctedPosition, correctedRotation);
        ApplyAnimation(frame.MoveSpeed);
        if (frame.IsPlayer) GetComponent<GrayboxPlayerController>()?.ApplyNetworkMotion(frame.Action, frame.IsFiring && !latestDead, frame.Dead || latestDead);
    }

    public void ApplyPredictedState(Vector3 position, float rotationY, float moveSpeed)
    {
        ApplyPredictedState(position, rotationY, moveSpeed, Vector3.zero);
    }

    public void ApplyPredictedState(Vector3 position, float rotationY, float moveSpeed, Vector3 velocity)
    {
        ClearExtrapolationCorrection();
        snapshotPlayback = false;
        snapshots.Clear();
        ApplyTransformState(position, rotationY, moveSpeed, velocity);
    }

    public void SnapTo(Vector3 position, float rotationY, float moveSpeed)
    {
        ClearExtrapolationCorrection();
        snapshotPlayback = false;
        snapshots.Clear();
        targetPosition = position;
        targetRotation = Quaternion.Euler(0f, rotationY, 0f);
        if (presentationDriver != null) presentationDriver.SetPredictedPose(targetPosition, targetRotation, Vector3.zero, true);
        else transform.SetPositionAndRotation(targetPosition, targetRotation);
        initialized = true;
        ApplyAnimation(moveSpeed);
    }

    public void PlayAttack()
    {
        if (animator != null && hasAttack)
        {
            animator.SetTrigger(AttackHash);
        }
    }

    public void StopInterpolation()
    {
        ClearExtrapolationCorrection();
        snapshots.Clear();
        snapshotPlayback = false;
        initialized = false;
        enabled = false;
    }

    private void ApplyTransformState(Vector3 position, float rotationY, float moveSpeed, Vector3 velocity)
    {
        targetPosition = position;
        targetRotation = Quaternion.Euler(0f, rotationY, 0f);

        bool hardSnap = !initialized || Vector3.Distance(transform.position, targetPosition) > snapDistance;
        if (localAuthorityView && presentationDriver != null)
        {
            presentationDriver.SetPredictedPose(targetPosition, targetRotation, velocity, hardSnap);
        }
        else if (hardSnap || localAuthorityView)
        {
            transform.SetPositionAndRotation(targetPosition, targetRotation);
        }

        initialized = true;

        ApplyAnimation(moveSpeed);
    }

    private void ApplyAnimation(float moveSpeed)
    {
        GrayboxPlayerController playerView = GetComponent<GrayboxPlayerController>();
        if (playerView != null && playerView.IsNetworkView)
            return;
        if (animator != null)
        {
            float normalizedSpeed = moveSpeed > 0.05f ? 1f : 0f;

            if (hasMoveX)
            {
                animator.SetFloat(MoveXHash, 0f);
            }

            if (hasMoveY)
            {
                animator.SetFloat(MoveYHash, normalizedSpeed);
            }

            if (hasSpeed)
            {
                animator.SetFloat(SpeedHash, normalizedSpeed);
            }
        }
    }

    private void Update()
    {
        if (!initialized)
        {
            return;
        }

        if (snapshotPlayback)
        {
            TickRemotePresentation(Time.unscaledDeltaTime);
            return;
        }
        if (localAuthorityView) return;

        float blend = 1f - Mathf.Exp(-interpolationRate * Time.unscaledDeltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPosition, blend);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, blend);

        if ((transform.position - targetPosition).sqrMagnitude < 0.0001f)
        {
            transform.position = targetPosition;
        }

        if (Quaternion.Angle(transform.rotation, targetRotation) < 0.1f)
        {
            transform.rotation = targetRotation;
        }
    }

    /// <summary>
    /// 推进远程显示。公开这个小入口是为了让自动化测试不依赖真实帧率；业务代码仍由 Update 调用。
    /// 它只改当前模型 Transform，不更新 NetworkCharacterWorld 中的权威碰撞代理。
    /// </summary>
    public void TickRemotePresentation(float deltaTime)
    {
        if (!snapshotPlayback || !initialized)
            return;
        if (snapshots.Advance(deltaTime, snapshotDelay, out InterpolationSnapshot sample, maxExtrapolationTime))
            RenderSnapshot(sample);

        // 指数衰减与帧率无关。rate=20 时大约 0.15 秒能消除 95% 的位置/旋转误差。
        float decay = Mathf.Exp(-extrapolationCorrectionRate * Mathf.Max(0f, deltaTime));
        correctionOffset *= decay;
        correctionRotation = Quaternion.Slerp(Quaternion.identity, correctionRotation, decay);
        if (correctionOffset.sqrMagnitude < 0.000001f)
            correctionOffset = Vector3.zero;
    }

    private void ClearExtrapolationCorrection()
    {
        pendingExtrapolationCorrection = false;
        correctionOffset = Vector3.zero;
        correctionRotation = Quaternion.identity;
    }

    private Vector3 ConstrainDisplayToWorld(Vector3 start, Vector3 desired)
    {
        Vector3 delta = desired - start;
        float distance = delta.magnitude;
        if (distance < 0.0001f)
            return desired;

        NetworkEntity entity = GetComponent<NetworkEntity>();
        NetworkCharacterShape shape = NetworkCharacterShape.ForPrefab(entity != null ? entity.PrefabId : NetworkPrefabCatalog.PlayerPrefabId);
        // 半径按 SkinWidth 近似收缩，避免站在地面上时 CapsuleCast 立刻报告零距离命中。
        int hitCount = Physics.CapsuleCastNonAlloc(start + Vector3.up * shape.Radius,
            start + Vector3.up * (shape.Height - shape.Radius), Mathf.Max(0.05f, shape.Radius - 0.08f),
            delta / distance, extrapolationHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float allowedDistance = distance;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = extrapolationHits[i];
            if (hit.collider == null || NetworkCharacterWorld.IsCharacterCollider(hit.collider) || hit.collider.attachedRigidbody != null ||
                hit.collider.transform.IsChildOf(transform) || hit.normal.y > 0.7f)
                continue;
            allowedDistance = Mathf.Min(allowedDistance, Mathf.Max(0f, hit.distance - 0.02f));
        }
        return start + delta / distance * allowedDistance;
    }

    private void CacheAnimatorParameters()
    {
        hasMoveX = false;
        hasMoveY = false;
        hasSpeed = false;
        hasAttack = false;

        if (animator == null)
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            hasMoveX |= parameter.nameHash == MoveXHash && parameter.type == AnimatorControllerParameterType.Float;
            hasMoveY |= parameter.nameHash == MoveYHash && parameter.type == AnimatorControllerParameterType.Float;
            hasSpeed |= parameter.nameHash == SpeedHash && parameter.type == AnimatorControllerParameterType.Float;
            hasAttack |= parameter.nameHash == AttackHash && parameter.type == AnimatorControllerParameterType.Trigger;
        }
    }
}
