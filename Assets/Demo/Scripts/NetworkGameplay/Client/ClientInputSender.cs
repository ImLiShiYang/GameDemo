using UnityEngine;

/// <summary>
/// 在客户端主线程采集本地玩家的键盘和鼠标输入，并按固定频率发送给游戏服务器。
/// 该组件只发送“玩家想做什么”，不会直接移动玩家；最终位置由服务器计算后通过世界快照返回。
/// 组件由 NetworkBootstrap 在客户端进入主场景后动态添加到 NetworkBootstrap GameObject 上。
/// </summary>
public sealed class ClientInputSender : MonoBehaviour
{
    // 已完成 Welcome 验证的客户端连接，用于发送 ClientInput 消息。
    private GameNetworkClient client;

    // 客户端本地玩家的表现对象。这里只用它的位置和朝向计算鼠标瞄准方向，不直接控制它移动。
    private Transform localPlayer;

    // 用于把 WASD 转成相机相对的世界方向，并把鼠标屏幕坐标投射到玩家所在的水平面。
    private Camera inputCamera;

    // 累计未发送的时间，使输入发送频率不依赖客户端画面帧率。
    private float sendAccumulator;

    // 每发出一条输入就递增。服务器用它丢弃重复或过期的输入。
    private uint inputSequence;

    // 客户端自己的输入时钟。目前随每次发送递增，保留给后续预测和服务器校正使用。
    private uint clientTick;

    /// <summary>
    /// 主场景准备完成后由 NetworkBootstrap 调用，注入网络连接和本地玩家表现对象。
    /// </summary>
    public void Initialize(GameNetworkClient networkClient, Transform localPlayerTransform)
    {
        client = networkClient;
        localPlayer = localPlayerTransform;
        inputCamera = Camera.main;
    }

    private void Update()
    {
        // 没有通过游戏服务器验证，或游戏处于暂停状态时，不发送操作输入。
        if (client == null || !client.IsWelcomed || Time.timeScale <= 0f)
        {
            return;
        }

        // 使用 unscaledDeltaTime，让发送频率不受 Time.timeScale 数值影响。
        // 当前 DefaultTickRate 为 20，因此正常情况下每 0.05 秒发送一次。
        sendAccumulator += Time.unscaledDeltaTime;
        float sendInterval = 1f / NetworkRuntime.DefaultTickRate;

        // 一帧耗时过长时可能需要补发多次，避免输入时钟因为掉帧而明显变慢。
        while (sendAccumulator >= sendInterval)
        {
            sendAccumulator -= sendInterval;
            SendCurrentInput();
        }
    }

    private void SendCurrentInput()
    {
        // movement 和 aim 都是方向/意图，不是客户端声明的位置。
        Vector3 movement = GetCameraRelativeMovement();
        Vector3 aim = GetAimDirection();
        ClientInputButtons buttons = ClientInputButtons.None;

        // 按钮用位标记合并到一个字节中，可以同时表达开火、翻滚等多个状态。
        if (Input.GetMouseButton(0))
        {
            buttons |= ClientInputButtons.Fire;
        }

        if (Input.GetKey(KeyCode.Space))
        {
            buttons |= ClientInputButtons.Roll;
        }

        // 服务器收到后会校验序号、限制方向长度，并在自己的 Tick 中计算权威位置。
        client.SendClientInput(new ClientInputMessage
        {
            Sequence = ++inputSequence,
            ClientTick = ++clientTick,
            Horizontal = movement.x,
            Vertical = movement.z,
            AimX = aim.x,
            AimZ = aim.z,
            Buttons = buttons
        });
    }

    private Vector3 GetCameraRelativeMovement()
    {
        // Horizontal/Vertical 对应项目 Input Manager 中的 A/D 和 W/S。
        float horizontal = Input.GetAxisRaw("Horizontal");
        float vertical = Input.GetAxisRaw("Vertical");
        Transform cameraTransform = inputCamera != null ? inputCamera.transform : null;

        // 去掉相机方向的高度分量，只保留地面 XZ 平面上的前方和右方。
        Vector3 forward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
        Vector3 right = cameraTransform != null ? cameraTransform.right : Vector3.right;
        forward.y = 0f;
        right.y = 0f;
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;

        // 限制长度为 1，避免同时按 W+D 时沿对角线移动得更快。
        return Vector3.ClampMagnitude(forward * vertical + right * horizontal, 1f);
    }

    private Vector3 GetAimDirection()
    {
        if (inputCamera != null && localPlayer != null)
        {
            // 从鼠标位置发出一条摄像机射线，与经过玩家位置的水平面求交点。
            Ray ray = inputCamera.ScreenPointToRay(Input.mousePosition);
            Plane plane = new Plane(Vector3.up, localPlayer.position);

            if (plane.Raycast(ray, out float distance))
            {
                // 交点减去玩家位置，得到玩家在地面上应该面向的方向。
                Vector3 direction = ray.GetPoint(distance) - localPlayer.position;
                direction.y = 0f;

                if (direction.sqrMagnitude > 0.0001f)
                {
                    return direction.normalized;
                }
            }
        }

        // 摄像机、玩家或有效交点不存在时，沿用玩家当前朝向，保证发给服务器的方向始终有效。
        Vector3 fallback = localPlayer != null ? localPlayer.forward : Vector3.forward;
        fallback.y = 0f;
        return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
    }
}
