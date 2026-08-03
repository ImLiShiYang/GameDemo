using UnityEngine;
using UnityEngine.Serialization;

[RequireComponent(typeof(CharacterController))]
public class GrayboxPlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 3.2f;

    [FormerlySerializedAs("moveSpeed")]
    [SerializeField] private float runSpeed = 5f;

    [SerializeField] private float turnSpeed = 12f;
    [SerializeField] private float acceleration = 18f;
    [SerializeField] private KeyCode runKey = KeyCode.LeftShift;

    [Header("Mouse Ground Aim")]
    [SerializeField] private Camera aimCamera;
    [SerializeField] private LayerMask aimGroundMask = ~0;
    [SerializeField] private float aimRayDistance = 500f;
    [SerializeField] private float aimTurnSpeed = 1080f;

    [Header("References")]
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Animator animator;

    private CharacterController characterController;
    private Vector3 currentMoveDirection;
    private readonly RaycastHit[] aimHits = new RaycastHit[16];

    public Vector3 AimPoint { get; private set; }
    public bool HasAimPoint { get; private set; }

    private static readonly int SpeedHash =
        Animator.StringToHash("Speed");

    private static readonly int FireHash =
        Animator.StringToHash("Fire");

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
    }

    private void Update()
    {
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");

        Vector3 forward = cameraTransform.forward;
        Vector3 right = cameraTransform.right;

        forward.y = 0f;
        right.y = 0f;

        forward.Normalize();
        right.Normalize();

        Vector3 targetMoveDirection =
            forward * vertical +
            right * horizontal;

        targetMoveDirection =
            Vector3.ClampMagnitude(targetMoveDirection, 1f);

        currentMoveDirection = Vector3.MoveTowards(
            currentMoveDirection,
            targetMoveDirection,
            acceleration * Time.deltaTime
        );

        bool hasMoveInput = targetMoveDirection.sqrMagnitude > 0.01f;
        bool isRunning = hasMoveInput && Input.GetKey(runKey);
        float targetAnimationSpeed = hasMoveInput
            ? (isRunning ? 1f : 0.5f)
            : 0f;

        if (animator != null)
        {
            animator.SetFloat(
                SpeedHash,
                targetAnimationSpeed,
                0.08f,
                Time.deltaTime
            );

            animator.SetBool(
                FireHash,
                Input.GetMouseButton(0)
            );
        }

        float currentSpeed = isRunning ? runSpeed : walkSpeed;
        characterController.SimpleMove(
            currentMoveDirection * currentSpeed
        );

        Vector3 facingDirection;
        float rotationSpeed;

        if (TryGetMouseAimDirection(out Vector3 aimDirection))
        {
            facingDirection = aimDirection;
            rotationSpeed = aimTurnSpeed;
        }
        else
        {
            facingDirection = currentMoveDirection;
            rotationSpeed = turnSpeed * 60f;
        }

        if (facingDirection.sqrMagnitude > 0.01f)
        {
            Quaternion targetRotation =
                Quaternion.LookRotation(facingDirection);

            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );
        }
    }

    private bool IsPointerInsideGameView()
    {
#if UNITY_EDITOR
        UnityEditor.EditorWindow window =
            UnityEditor.EditorWindow.mouseOverWindow;

        if (window == null ||
            window.GetType().Name != "GameView")
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
    
    private bool TryGetMouseAimDirection(out Vector3 direction)
    {
        direction = Vector3.zero;

        if (aimCamera == null)
        {
            HasAimPoint = false;
            return false;
        }

        if (!IsPointerInsideGameView())
        {
            direction = AimPoint - transform.position;
            direction.y = 0f;

            if (!HasAimPoint || direction.sqrMagnitude < 0.0001f)
                return false;

            direction.Normalize();
            return true;
        }

        HasAimPoint = false;

        Ray ray = aimCamera.ScreenPointToRay(Input.mousePosition);
        int hitCount = Physics.RaycastNonAlloc(
            ray,
            aimHits,
            aimRayDistance,
            aimGroundMask,
            QueryTriggerInteraction.Ignore
        );

        float nearestDistance = float.PositiveInfinity;
        bool foundGroundHit = false;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = aimHits[i];

            if (hit.collider == null ||
                hit.collider.transform.root == transform.root ||
                hit.normal.y < 0.2f ||
                hit.distance >= nearestDistance)
            {
                continue;
            }

            nearestDistance = hit.distance;
            AimPoint = hit.point;
            foundGroundHit = true;
        }

        if (!foundGroundHit)
        {
            Plane groundPlane = new Plane(Vector3.up, transform.position);
            if (!groundPlane.Raycast(ray, out float enter))
            {
                return false;
            }

            AimPoint = ray.GetPoint(enter);
        }

        direction = AimPoint - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        direction.Normalize();
        HasAimPoint = true;
        return true;
    }

    private void OnDrawGizmosSelected()
    {
        if (!HasAimPoint)
        {
            return;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawLine(transform.position, AimPoint);
        Gizmos.DrawSphere(AimPoint, 0.12f);
    }
}