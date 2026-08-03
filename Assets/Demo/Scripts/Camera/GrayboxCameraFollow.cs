using UnityEngine;

public class GrayboxCameraFollow : MonoBehaviour
{
    [Header("Follow Camera")]
    [SerializeField] private Transform target;

    [SerializeField]
    private Vector3 offset = new Vector3(0f, 13f, -10f);

    [SerializeField] private float focusHeight = 1f;
    [SerializeField] private float smoothTime = 0.12f;

    [Header("Free View Toggle")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F1;
    [SerializeField] private GrayboxPlayerController playerController;
    [SerializeField] private bool pausePlayerMovement = true;

    [Header("Free View Controls")]
    [SerializeField] private float freeMoveSpeed = 8f;
    [SerializeField] private float sprintMultiplier = 3f;
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float scrollSpeed = 5f;

    private Vector3 followVelocity;
    private Vector3 smoothedFocusPoint;
    private bool isFreeView;
    private bool playerControllerWasEnabled;
    private float freeYaw;
    private float freePitch;

    public bool IsFreeView => isFreeView;

    private void Awake()
    {
        if (playerController == null)
        {
            playerController = FindObjectOfType<GrayboxPlayerController>();
        }

        playerControllerWasEnabled =
            playerController != null && playerController.enabled;

        if (target != null)
        {
            smoothedFocusPoint = GetTargetFocusPoint();
            ApplyStableFollowTransform();
        }

        SyncFreeViewAngles();
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
        {
            ToggleFreeView();
        }

        if (isFreeView)
        {
            UpdateFreeView();
        }
    }

    private void LateUpdate()
    {
        if (isFreeView || target == null)
        {
            return;
        }

        smoothedFocusPoint = Vector3.SmoothDamp(
            smoothedFocusPoint,
            GetTargetFocusPoint(),
            ref followVelocity,
            smoothTime
        );

        ApplyStableFollowTransform();
    }

    public void ToggleFreeView()
    {
        SetFreeView(!isFreeView);
    }

    public void SetFreeView(bool enabled)
    {
        if (isFreeView == enabled)
        {
            return;
        }

        isFreeView = enabled;
        followVelocity = Vector3.zero;

        if (isFreeView)
        {
            SyncFreeViewAngles();

            if (pausePlayerMovement && playerController != null)
            {
                playerControllerWasEnabled = playerController.enabled;
                playerController.enabled = false;
            }

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else if (pausePlayerMovement && playerController != null)
        {
            playerController.enabled = playerControllerWasEnabled;
        }

        if (!isFreeView && target != null)
        {
            smoothedFocusPoint =
                transform.position - GetCameraOffsetFromFocus();
        }
    }

    private void UpdateFreeView()
    {
        bool rotating = Input.GetMouseButton(1);

        if (rotating)
        {
            freeYaw += Input.GetAxis("Mouse X") * mouseSensitivity;
            freePitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
            freePitch = Mathf.Clamp(freePitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(freePitch, freeYaw, 0f);
        }

        Cursor.lockState = rotating
            ? CursorLockMode.Locked
            : CursorLockMode.None;
        Cursor.visible = !rotating;

        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        float verticalMovement = 0f;

        if (Input.GetKey(KeyCode.E))
        {
            verticalMovement += 1f;
        }

        if (Input.GetKey(KeyCode.Q))
        {
            verticalMovement -= 1f;
        }

        Vector3 movement =
            transform.right * horizontal +
            transform.forward * vertical +
            Vector3.up * verticalMovement;

        movement = Vector3.ClampMagnitude(movement, 1f);

        float speed = freeMoveSpeed;
        if (Input.GetKey(KeyCode.LeftShift) ||
            Input.GetKey(KeyCode.RightShift))
        {
            speed *= sprintMultiplier;
        }

        transform.position += movement * speed * Time.deltaTime;
        transform.position +=
            transform.forward * Input.mouseScrollDelta.y * scrollSpeed;
    }

    private void SyncFreeViewAngles()
    {
        Vector3 angles = transform.eulerAngles;
        freeYaw = angles.y;
        freePitch = angles.x > 180f ? angles.x - 360f : angles.x;
    }

    private Vector3 GetTargetFocusPoint()
    {
        return target.position + Vector3.up * focusHeight;
    }

    private Vector3 GetCameraOffsetFromFocus()
    {
        return offset - Vector3.up * focusHeight;
    }

    private void ApplyStableFollowTransform()
    {
        Vector3 cameraOffset = GetCameraOffsetFromFocus();
        transform.position = smoothedFocusPoint + cameraOffset;
        transform.rotation =
            Quaternion.LookRotation(-cameraOffset, Vector3.up);
    }

    private void OnDisable()
    {
        if (isFreeView && pausePlayerMovement && playerController != null)
        {
            playerController.enabled = playerControllerWasEnabled;
        }

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}