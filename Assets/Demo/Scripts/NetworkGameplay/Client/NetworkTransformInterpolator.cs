using UnityEngine;

public sealed class NetworkTransformInterpolator : MonoBehaviour
{
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");

    [SerializeField, Min(1f)] private float interpolationRate = 15f;
    [SerializeField, Min(0.1f)] private float snapDistance = 5f;

    private Animator animator;
    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private bool initialized;
    private bool localAuthorityView;

    public void Initialize(bool isLocalPlayer)
    {
        localAuthorityView = isLocalPlayer;
        animator = GetComponentInChildren<Animator>();
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
            animator.SetFloat(MoveXHash, 0f);
            animator.SetFloat(MoveYHash, moveSpeed > 0.05f ? 1f : 0f);
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
    }
}
