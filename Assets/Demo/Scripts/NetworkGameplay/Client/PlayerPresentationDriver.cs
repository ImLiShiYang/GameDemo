using UnityEngine;

/// <summary>
/// 将固定 Tick 的 SimulationRoot 与逐帧显示的模型分离。该组件只移动 VisualRoot，绝不写回碰撞和权威状态。
/// </summary>
[DefaultExecutionOrder(10000)]
public sealed class PlayerPresentationDriver : MonoBehaviour
{
    private const float DefaultHalfLife = 0.06f;
    private const float MaximumPredictionTime = 1f / NetworkRuntime.DefaultTickRate;
    private readonly RaycastHit[] extrapolationHits = new RaycastHit[16];

    [SerializeField, Min(0.01f)] private float correctionHalfLife = DefaultHalfLife;
    [SerializeField, Min(0.1f)] private float hardSnapDistance = 1f;
    [SerializeField] private bool drawDebug;

    private Transform visualRoot;
    private Transform cameraAnchor;
    private Transform upperBody;
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private Vector3 displayPosition;
    private Quaternion displayRotation;
    private Vector3 velocity;
    private float lastTargetTime;
    private float hitVisualUntil;
    private float hitVisualStarted;
    private Vector2 hitDirection;
    private PlayerHitKind hitKind;
    private Quaternion appliedHitRotation = Quaternion.identity;
    private bool hasAppliedHitRotation;
    private bool initialized;
    private bool localPrediction;

    public Transform VisualRoot => visualRoot;
    public Transform CameraAnchor => cameraAnchor != null ? cameraAnchor : visualRoot;
    public float PositionError => initialized ? Vector3.Distance(displayPosition, transform.position) : 0f;

    public void Initialize(bool isLocalPrediction)
    {
        Initialize(isLocalPrediction, null);
    }

    public void Initialize(bool isLocalPrediction, Transform visualModel)
    {
        localPrediction = isLocalPrediction;
        if (!initialized) BuildVisualHierarchy(visualModel);
        SnapVisualToSimulation();
    }

    public void SetPredictedPose(Vector3 position, Quaternion rotation, Vector3 horizontalVelocity, bool hardSnap)
    {
        if (!initialized) BuildVisualHierarchy(null);
        Vector3 previousDisplayPosition = visualRoot.position;
        Quaternion previousDisplayRotation = visualRoot.rotation;
        transform.SetPositionAndRotation(position, rotation);
        velocity = Vector3.ProjectOnPlane(horizontalVelocity, Vector3.up);
        lastTargetTime = Time.unscaledTime;
        if (hardSnap || Vector3.Distance(previousDisplayPosition, position) >= hardSnapDistance)
        {
            SnapVisualToSimulation();
            return;
        }
        displayPosition = previousDisplayPosition;
        displayRotation = previousDisplayRotation;
        ApplyVisualPose();
    }

    public void SetInterpolatedPose(Vector3 position, Quaternion rotation)
    {
        if (!initialized) BuildVisualHierarchy(null);
        transform.SetPositionAndRotation(position, rotation);
        velocity = Vector3.zero;
        displayPosition = position;
        displayRotation = rotation;
        ApplyVisualPose();
    }

    public void PlayHit(uint sequence, Vector2 direction, PlayerHitKind kind)
    {
        RemoveAppliedHitRotation();
        hitDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
        hitKind = kind;
        hitVisualStarted = Time.unscaledTime;
        float duration = kind == PlayerHitKind.Lethal ? PlayerMotionProfile.Runtime.DeathImpactDuration :
            PlayerMotionProfile.Runtime.NormalHitVisualDuration;
        hitVisualUntil = hitVisualStarted + duration;
    }

    public void SnapVisualToSimulation()
    {
        if (!initialized) BuildVisualHierarchy(null);
        displayPosition = transform.TransformPoint(baseLocalPosition);
        displayRotation = transform.rotation * baseLocalRotation;
        velocity = Vector3.zero;
        lastTargetTime = Time.unscaledTime;
        ApplyVisualPose();
    }

    private void BuildVisualHierarchy(Transform visualModel)
    {
        Animator animator = GetComponentInChildren<Animator>(true);
        Transform model = visualModel != null ? visualModel : animator != null ? animator.transform : null;
        if (animator != null && animator.isHuman)
            upperBody = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
        if (model == null || model == transform)
        {
            visualRoot = transform;
            cameraAnchor = transform;
            initialized = true;
            return;
        }

        if (model.parent != null && model.parent.parent == transform && model.parent.name == "VisualRoot")
        {
            visualRoot = model.parent;
            cameraAnchor = visualRoot.Find("CameraAnchor");
            if (cameraAnchor == null)
            {
                GameObject existingAnchor = new GameObject("CameraAnchor");
                cameraAnchor = existingAnchor.transform;
                cameraAnchor.SetParent(visualRoot, false);
            }
            baseLocalPosition = visualRoot.localPosition;
            baseLocalRotation = visualRoot.localRotation;
            initialized = true;
            return;
        }

        GameObject wrapper = new GameObject("VisualRoot");
        visualRoot = wrapper.transform;
        visualRoot.SetParent(transform, false);
        baseLocalPosition = Vector3.zero;
        baseLocalRotation = Quaternion.identity;
        model.SetParent(visualRoot, true);

        GameObject anchor = new GameObject("CameraAnchor");
        cameraAnchor = anchor.transform;
        cameraAnchor.SetParent(visualRoot, false);
        initialized = true;
    }

    private void Update()
    {
        // LateUpdate 添加的受击偏转必须在下一次 Animator 求值前撤销。
        // 某些 Locomotion Clip 不会每帧写 UpperChest；若直接连续 *=，偏转会累计成整圈旋转。
        RemoveAppliedHitRotation();
        TickPresentation(Time.unscaledDeltaTime);
    }

    public void TickPresentation(float deltaTime)
    {
        if (!initialized || visualRoot == transform) return;
        Vector3 desiredPosition = transform.TransformPoint(baseLocalPosition);
        Quaternion desiredRotation = transform.rotation * baseLocalRotation;
        if (localPrediction && velocity.sqrMagnitude > 0.0001f)
        {
            float predictionTime = Mathf.Clamp(Time.unscaledTime - lastTargetTime, 0f, MaximumPredictionTime);
            desiredPosition = ConstrainToWorld(desiredPosition, desiredPosition + velocity * predictionTime);
        }
        float decay = Mathf.Pow(0.5f, Mathf.Max(0f, deltaTime) / Mathf.Max(0.001f, correctionHalfLife));
        displayPosition = Vector3.Lerp(desiredPosition, displayPosition, decay);
        displayRotation = Quaternion.Slerp(desiredRotation, displayRotation, decay);
        ApplyVisualPose();
    }

    private void ApplyVisualPose()
    {
        if (visualRoot == null) return;
        visualRoot.SetPositionAndRotation(displayPosition, displayRotation);
    }

    private void LateUpdate()
    {
        if (upperBody == null || Time.unscaledTime >= hitVisualUntil) return;
        Quaternion hitRotation = Quaternion.identity;
        if (Time.unscaledTime < hitVisualUntil)
        {
            float duration = Mathf.Max(0.001f, hitVisualUntil - hitVisualStarted);
            float phase = Mathf.Clamp01((Time.unscaledTime - hitVisualStarted) / duration);
            float weight = Mathf.Sin(phase * Mathf.PI);
            float strength = hitKind == PlayerHitKind.Heavy ? 10f : hitKind == PlayerHitKind.Lethal ? 7f : 5f;
            hitRotation = Quaternion.Euler(-hitDirection.y * strength * weight, 0f, hitDirection.x * strength * weight);
        }
        // Animator 每帧先写入基础姿势，这里只在胸腔追加一个短促偏转；Root、Hips 和双腿完全不参与。
        upperBody.localRotation *= hitRotation;
        appliedHitRotation = hitRotation;
        hasAppliedHitRotation = true;
    }

    private void OnDisable()
    {
        RemoveAppliedHitRotation();
    }

    private void RemoveAppliedHitRotation()
    {
        if (!hasAppliedHitRotation)
        {
            return;
        }

        if (upperBody != null)
        {
            upperBody.localRotation *= Quaternion.Inverse(appliedHitRotation);
        }

        appliedHitRotation = Quaternion.identity;
        hasAppliedHitRotation = false;
    }

    private Vector3 ConstrainToWorld(Vector3 start, Vector3 desired)
    {
        Vector3 delta = desired - start;
        float distance = delta.magnitude;
        if (distance < 0.0001f) return desired;
        NetworkEntity entity = GetComponent<NetworkEntity>();
        NetworkCharacterShape shape = NetworkCharacterShape.ForPrefab(entity != null ? entity.PrefabId : NetworkPrefabCatalog.PlayerPrefabId);
        int hitCount = Physics.CapsuleCastNonAlloc(start + Vector3.up * shape.Radius,
            start + Vector3.up * (shape.Height - shape.Radius), Mathf.Max(0.05f, shape.Radius - 0.08f), delta / distance,
            extrapolationHits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float allowed = distance;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = extrapolationHits[i];
            if (hit.collider == null || NetworkCharacterWorld.IsCharacterCollider(hit.collider) || hit.collider.attachedRigidbody != null ||
                hit.collider.transform.IsChildOf(transform) || hit.normal.y > 0.7f) continue;
            allowed = Mathf.Min(allowed, Mathf.Max(0f, hit.distance - 0.02f));
        }
        return start + delta / distance * allowed;
    }

    private void OnDrawGizmos()
    {
        if (!drawDebug || !initialized) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.18f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(displayPosition, 0.14f);
        Gizmos.DrawLine(transform.position, displayPosition);
    }
}
