using UnityEngine;

[DefaultExecutionOrder(100)]
public class AimCrosshairView : MonoBehaviour
{
    [Header("References")]
    [Tooltip("提供鼠标世界交点的角色控制器。")]
    [SerializeField]
    private GrayboxPlayerController aimSource;

    [Tooltip("角色根节点。准星图案会朝向这个对象。")]
    [SerializeField]
    private Transform playerTransform;

    [Header("Scale")]
    [SerializeField]
    private float referenceDistance = 10f;

    [SerializeField]
    private float referenceScale = 0.5f;

    [SerializeField]
    private float minScale = 0.2f;

    [SerializeField]
    private float maxScale = 1.2f;
    
    [Header("Cursor")]
    [Tooltip("运行游戏并聚焦 Game 窗口时隐藏系统鼠标。")]
    [SerializeField]
    private bool hideSystemCursor = true;

    [Tooltip("把鼠标限制在游戏窗口内，避免移动到窗口外。")]
    [SerializeField]
    private bool confineCursorToGameWindow = true;
    
    [Tooltip("拖入子物体 Quad。")]
    [SerializeField]
    private GameObject visualRoot;

    [Header("Position")]
    [Tooltip("0 表示立即跟随鼠标位置。")]
    [SerializeField, Min(0f)]
    private float followSpeed = 0f;

    [Header("Rotation")]
    [Tooltip("准星贴图方向不正确时，用这个角度进行修正。")]
    [SerializeField]
    private float yawOffset = 0f;

    [Tooltip("是否让准星图案朝向角色。")]
    [SerializeField]
    private bool facePlayer = true;

    private bool hasInitializedPosition;

    private void Awake()
    {
        /*
         * 没有手动设置角色 Transform 时，
         * 默认使用 GrayboxPlayerController 所在物体。
         */
        if (playerTransform == null && aimSource != null)
        {
            playerTransform = aimSource.transform;
        }

        SetVisible(false);
    }

    private void OnEnable()
    {
        ApplyCursorState(true);
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        /*
         * Game 窗口获得焦点：
         * 隐藏系统鼠标。
         *
         * 游戏失去焦点：
         * 恢复鼠标，方便操作编辑器或其他窗口。
         */
        ApplyCursorState(hasFocus);
    }

    private void OnDisable()
    {
        RestoreCursor();
    }

    private void OnDestroy()
    {
        RestoreCursor();
    }
    
    private void LateUpdate()
    {
        if (aimSource == null ||
            visualRoot == null ||
            aimSource.IsRolling ||
            !aimSource.TryGetCrosshairWorldPoint(out Vector3 crosshairWorldPoint))
        {
            SetVisible(false);
            return;
        }

        SetVisible(true);

        UpdatePosition(crosshairWorldPoint);

        if (facePlayer)
        {
            FacePlayer();
        }

        UpdateScale();
    }

    private void ApplyCursorState(bool gameHasFocus)
    {
        bool shouldHide =
            hideSystemCursor &&
            gameHasFocus;

        Cursor.visible = !shouldHide;

        if (shouldHide && confineCursorToGameWindow)
        {
            /*
             * Confined 只是把鼠标限制在游戏窗口中，
             * 不会把鼠标固定在屏幕中心。
             */
            Cursor.lockState = CursorLockMode.Confined;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
        }
    }

    private void RestoreCursor()
    {
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }
    
    private void UpdateScale()
    {
        Camera mainCamera = Camera.main;

        if (mainCamera == null)
        {
            return;
        }

        float distance = Vector3.Distance(
            mainCamera.transform.position,
            transform.position
        );

        float scale =
            referenceScale *
            distance /
            Mathf.Max(referenceDistance, 0.01f);

        scale = Mathf.Clamp(
            scale,
            minScale,
            maxScale
        );

        transform.localScale =
            Vector3.one * scale;
    }
    
    /// <summary>
    /// 将世界空间准心移动到当前枪械实际瞄准目标点。
    /// 准心与 AimTarget 使用同一个世界坐标，保证准心和枪械瞄准方向保持一致。
    /// </summary>
    private void UpdatePosition(Vector3 targetPosition)
    {
        if (!hasInitializedPosition || followSpeed <= 0f)
        {
            transform.position = targetPosition;
            hasInitializedPosition = true;
            return;
        }

        float interpolation = 1f - Mathf.Exp(-followSpeed * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, targetPosition, interpolation);
    }

    private void FacePlayer()
    {
        if (playerTransform == null)
        {
            return;
        }

        /*
         * 从准星位置指向角色位置。
         * 清除 Y，保证准星只绕世界 Y 轴旋转，
         * 不会向上或向下倾斜。
         */
        Vector3 directionToPlayer =
            playerTransform.position - transform.position;

        directionToPlayer.y = 0f;

        if (directionToPlayer.sqrMagnitude < 0.0001f)
        {
            return;
        }

        Quaternion lookRotation = Quaternion.LookRotation(
            directionToPlayer.normalized,
            Vector3.up
        );

        /*
         * yawOffset 用于修正贴图本身的正方向。
         * 常用值：0、90、-90、180。
         */
        Quaternion offsetRotation =
            Quaternion.Euler(0f, yawOffset, 0f);

        transform.rotation =
            lookRotation * offsetRotation;
    }

    private void SetVisible(bool visible)
    {
        if (visualRoot == null)
        {
            return;
        }

        if (visualRoot.activeSelf != visible)
        {
            visualRoot.SetActive(visible);
        }
    }
}
