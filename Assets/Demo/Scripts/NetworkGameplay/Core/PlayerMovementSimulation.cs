using UnityEngine;

/// <summary>
/// 客户端预测与服务器权威模拟共用的固定 Tick 玩家移动算法。
/// 不读取输入、不访问物理系统，也不播放任何表现。
/// </summary>
public static class PlayerMovementSimulation
{
    public const float MoveSpeed = 3.2f;
    public static float TickDeltaTime => 1f / NetworkRuntime.DefaultTickRate;

    public static void Step(ref Vector3 position, ref float rotationY, Vector2 moveInput, Vector2 aimInput)
    {
        Vector2 movement = Vector2.ClampMagnitude(moveInput, 1f);
        position += new Vector3(movement.x, 0f, movement.y) * (MoveSpeed * TickDeltaTime);

        Vector2 aim = Vector2.ClampMagnitude(aimInput, 1f);

        if (aim.sqrMagnitude > 0.0001f)
        {
            rotationY = Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg;
        }
    }
}
