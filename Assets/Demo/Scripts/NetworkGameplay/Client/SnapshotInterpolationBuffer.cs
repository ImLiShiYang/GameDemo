using System;
using UnityEngine;

public struct InterpolationSnapshot
{
    public uint Tick;
    public Vector3 Position;
    public Quaternion Rotation;
    public float MoveSpeed;
    public bool IsPlayer;
    public PlayerActionState Action;
    public bool IsFiring;
    public bool Dead;
    // 普通实体已经同步服务器速度；玩家没有该字段，因此玩家外推会用最近两帧位置估算速度。
    public Vector3 Velocity;
    public bool HasVelocity;
}

/// <summary>固定容量服务器时间轴。渲染延后两个快照周期，不根据包到达间隔计算插值比例。</summary>
public sealed class SnapshotInterpolationBuffer
{
    public const int Capacity = 32;
    public const double DefaultDelay = 0.2;
    public const double DefaultExtrapolation = 0.15;
    public const float MaximumExtrapolationDistance = 1f;
    private const float MaximumHorizontalSpeed = 10f;
    private readonly InterpolationSnapshot[] frames = new InterpolationSnapshot[Capacity];
    private int first;
    public int Count { get; private set; }
    public double PlaybackTime { get; private set; }
    public bool Starved { get; private set; }
    public bool IsExtrapolating { get; private set; }
    public double ExtrapolatedSeconds { get; private set; }
    public Vector3 LatestPosition => Count == 0 ? Vector3.zero : At(Count - 1).Position;
    public double BufferedSeconds => Count == 0 ? 0 : TimeOf(At(Count - 1)) - PlaybackTime;
    private static double TimeOf(InterpolationSnapshot frame) => frame.Tick / (double)NetworkRuntime.DefaultTickRate;
    private InterpolationSnapshot At(int index) => frames[(first + index) % Capacity];

    public void Clear()
    {
        first = 0;
        Count = 0;
        PlaybackTime = 0;
        Starved = false;
        IsExtrapolating = false;
        ExtrapolatedSeconds = 0;
    }

    // 返回是否接受该快照；reset 表示首帧、传送或长时间中断后重新建立播放基线。
    public bool Push(InterpolationSnapshot frame, double delay, float teleportDistance, out bool reset)
    {
        reset = false;
        // 非法浮点数一旦进入时间轴，就会污染后续所有 Lerp 和速度计算，所以在入口直接拒绝。
        if (!Finite(frame.Position.x) || !Finite(frame.Position.y) || !Finite(frame.Position.z) ||
            !Finite(frame.Rotation.x) || !Finite(frame.Rotation.y) || !Finite(frame.Rotation.z) || !Finite(frame.Rotation.w) ||
            !Finite(frame.MoveSpeed) || frame.HasVelocity && (!Finite(frame.Velocity.x) || !Finite(frame.Velocity.y) || !Finite(frame.Velocity.z))) return false;
        if (Count > 0 && frame.Tick <= At(Count - 1).Tick)
            return false;

        delay = Math.Max(0.05, delay);

        if (Count == 0 || Vector3.Distance(frame.Position, At(Count - 1).Position) > teleportDistance ||
            TimeOf(frame) - TimeOf(At(Count - 1)) > Math.Max(1.0, delay * 4))
        {
            Clear();
            PlaybackTime = TimeOf(frame) - delay;
            reset = true;
        }

        if (Count == Capacity)
        {
            first = (first + 1) % Capacity;
            Count--;
        }
        frames[(first + Count) % Capacity] = frame;
        Count++;
        return true;
    }

    /// <summary>
    /// 按显示时间采样。播放时间位于两份真实快照之间时做插值；超过最新快照时只做有限外推。
    /// 返回值仅供远程模型显示，绝对不能写回权威位置、碰撞代理或伤害判定。
    /// maxExtrapolation 传 0 可以关闭外推，方便调试比较。
    /// </summary>
    public bool Advance(double deltaTime, double delay, out InterpolationSnapshot sample, double maxExtrapolation = DefaultExtrapolation)
    {
        sample = default;
        if (Count == 0) return false;
        if (double.IsNaN(deltaTime) || double.IsInfinity(deltaTime)) deltaTime = 0;
        maxExtrapolation = double.IsNaN(maxExtrapolation) ? 0 : Math.Max(0, Math.Min(0.25, maxExtrapolation));
        IsExtrapolating = false;
        ExtrapolatedSeconds = 0;
        delay = Math.Max(0.05, delay);
        double newest = TimeOf(At(Count - 1));
        double buffered = newest - PlaybackTime;
        // 温和调速维持缓冲，不在每次收包时重设时间，避免网络抖动直接变成画面抖动。
        double speed = buffered > delay + 0.1 ? 1.05 : buffered < delay - 0.1 ? 0.95 : 1.0;
        if (buffered > Math.Max(1.0, delay * 4)) PlaybackTime = newest - delay;
        // 外推以服务器时间为基准，不逐帧累加模型位移。到达上限后时钟停止，断线再久也不会无限前冲。
        PlaybackTime = Math.Min(newest + maxExtrapolation, PlaybackTime + Math.Max(0, deltaTime) * speed);
        Starved = PlaybackTime >= newest;
        while (Count > 2 && TimeOf(At(1)) <= PlaybackTime) { first = (first + 1) % Capacity; Count--; }
        InterpolationSnapshot from = At(0);
        if (Count == 1 || PlaybackTime <= TimeOf(from)) { sample = from; return true; }
        InterpolationSnapshot to = At(1);
        double duration = TimeOf(to) - TimeOf(from);
        if (PlaybackTime > newest)
        {
            ExtrapolatedSeconds = Math.Min(maxExtrapolation, PlaybackTime - newest);
            IsExtrapolating = true;
            sample = Extrapolate(from, to, duration, ExtrapolatedSeconds);
            return true;
        }
        float t = (float)Math.Max(0, Math.Min(1, (PlaybackTime - TimeOf(from)) / duration));
        // 动作是离散状态，不把未来的翻滚/开火提前应用到旧时间的位置上。
        sample = t >= 1f ? to : from;
        sample.Position = Vector3.Lerp(from.Position, to.Position, t);
        sample.Rotation = Quaternion.Slerp(from.Rotation, to.Rotation, t);
        sample.MoveSpeed = Mathf.Lerp(from.MoveSpeed, to.MoveSpeed, t);
        return true;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private static InterpolationSnapshot Extrapolate(InterpolationSnapshot previous, InterpolationSnapshot latest, double interval, double seconds)
    {
        // 从最新真实快照完整复制离散状态：外推只猜位置，不虚构开火、技能、受击或复活事件。
        InterpolationSnapshot result = latest;
        // 样本间隔过大时，平均速度已经不可靠；死亡和受击硬直也不应该继续沿旧方向滑动。
        if (interval <= 0 || interval > 0.25 || latest.Dead || latest.Action.HitStunTicks > 0) return result;
        bool rolling = latest.IsPlayer && latest.Action.IsRolling;
        // 服务器已声明玩家停止时，即使前两帧有位移，也不能沿旧速度继续外推。
        if (latest.IsPlayer && !rolling && latest.MoveSpeed <= 0.01f) return result;
        // 翻滚只允许预测到剩余 RollTicks 对应的时间，不能把一次翻滚无限延长。
        if (rolling) seconds = Math.Min(seconds, latest.Action.RollTicks * PlayerMovementSimulation.TickDeltaTime);
        Vector3 velocity = latest.HasVelocity ? latest.Velocity : (latest.Position - previous.Position) / (float)interval;
        // 当前没有完整的远程重力/落地预测。保守地冻结高度与朝向，避免角色浮空、钻地或瞄准方向乱转。
        velocity.y = 0f;
        float speedLimit = latest.IsPlayer && !rolling ? Mathf.Min(MaximumHorizontalSpeed, latest.MoveSpeed) : MaximumHorizontalSpeed;
        velocity = Vector3.ClampMagnitude(velocity, speedLimit);
        result.Position += Vector3.ClampMagnitude(velocity * (float)seconds, MaximumExtrapolationDistance);
        return result;
    }
}
