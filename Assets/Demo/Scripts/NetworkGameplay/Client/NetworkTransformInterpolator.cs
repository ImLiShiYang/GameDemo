using UnityEngine;

public sealed class NetworkTransformInterpolator : MonoBehaviour
{
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");
    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int AttackHash = Animator.StringToHash("Attack");

    [SerializeField, Min(1f)] private float interpolationRate = 15f;
    [SerializeField, Min(0.1f)] private float snapDistance = 5f;

    private Animator animator;
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
        CacheAnimatorParameters();
        targetPosition = transform.position;
        targetRotation = transform.rotation;
        initialized = false;
    }

    public void ApplyState(PlayerNetworkState state)
    {
        ApplyTransformState(state.Position, state.RotationY, state.MoveSpeed);
    }

    public void ApplyState(EntityNetworkState state)
    {
        ApplyTransformState(state.Position, state.RotationY, state.AnimationState == 0 ? 0f : 1f);
    }

    public void ApplySpawn(EntitySpawnMessage message)
    {
        targetPosition = message.Position;
        targetRotation = message.Rotation;
        transform.SetPositionAndRotation(targetPosition, targetRotation);
        initialized = true;
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
        initialized = false;
        enabled = false;
    }

    private void ApplyTransformState(Vector3 position, float rotationY, float moveSpeed)
    {
        targetPosition = position;
        targetRotation = Quaternion.Euler(0f, rotationY, 0f);

        if (!initialized || localAuthorityView || Vector3.Distance(transform.position, targetPosition) > snapDistance)
        {
            transform.SetPositionAndRotation(targetPosition, targetRotation);
        }

        initialized = true;

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
        if (!initialized || localAuthorityView)
        {
            return;
        }

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
