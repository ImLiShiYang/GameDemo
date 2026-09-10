using UnityEngine;

/// <summary>
/// 单机、客户端预测和服务器权威模拟共用的玩家运动参数。
/// Resources 中可放置同名资产覆盖默认值；曲线在编辑时烘焙为定长采样，运行时不读取 Animator Root Motion。
/// </summary>
[CreateAssetMenu(fileName = "PlayerMotionProfile", menuName = "Game/Network/Player Motion Profile")]
public sealed class PlayerMotionProfile : ScriptableObject
{
    private const string ResourceName = "PlayerMotionProfile";
    private static PlayerMotionProfile runtime;

    [Header("Locomotion")]
    [SerializeField, Min(0f)] private float moveSpeed = 3.2f;
    [SerializeField, Min(0f)] private float acceleration = 18f;
    [SerializeField, Min(0f)] private float deceleration = 22f;

    [Header("Roll")]
    [SerializeField, Min(1)] private int rollDurationTicks = 15;
    [SerializeField, Min(0)] private int rollCooldownTicks = 7;
    [SerializeField, Min(0f)] private float rollDistance = 4f;
    [Tooltip("从 Running Dive Roll 的 Root Motion 轨迹归一化后烘焙的距离采样。首尾必须为 0 和 1。")]
    [SerializeField] private float[] rollDistanceSamples =
    {
        0f, 0.018f, 0.058f, 0.119f, 0.198f, 0.292f, 0.397f, 0.508f,
        0.62f, 0.726f, 0.82f, 0.897f, 0.953f, 0.985f, 0.998f, 1f
    };

    [Header("Hit")]
    [SerializeField, Min(1)] private int normalHitStunTicks = 3;
    [SerializeField, Min(0f)] private float normalHitVisualDuration = 0.22f;
    [SerializeField, Min(0f)] private float deathImpactDuration = 0.08f;
    [SerializeField, Min(1)] private int heavyHitTicks = 6;
    [SerializeField, Min(0f)] private float heavyHitDistance = 1.5f;
    [SerializeField] private AnimationCurve heavyHitDistanceCurve =
        new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.35f, 0.8f), new Keyframe(1f, 1f));

    public static PlayerMotionProfile Runtime
    {
        get
        {
            if (runtime != null) return runtime;
            runtime = Resources.Load<PlayerMotionProfile>(ResourceName);
            if (runtime != null) return runtime;
            runtime = CreateInstance<PlayerMotionProfile>();
            runtime.hideFlags = HideFlags.HideAndDontSave;
            return runtime;
        }
    }

    public float MoveSpeed => moveSpeed;
    public float Acceleration => acceleration;
    public float Deceleration => deceleration;
    public int RollDurationTicks => rollDurationTicks;
    public int RollCooldownTicks => rollCooldownTicks;
    public float RollDistance => rollDistance;
    public int NormalHitStunTicks => normalHitStunTicks;
    public float NormalHitVisualDuration => normalHitVisualDuration;
    public float DeathImpactDuration => deathImpactDuration;
    public int HeavyHitTicks => heavyHitTicks;
    public float HeavyHitDistance => heavyHitDistance;

    public float EvaluateRollDistance(float normalizedTime) => EvaluateSamples(rollDistanceSamples, normalizedTime);
    public float EvaluateHeavyHitDistance(float normalizedTime) => Mathf.Clamp01(heavyHitDistanceCurve.Evaluate(Mathf.Clamp01(normalizedTime)));

    private static float EvaluateSamples(float[] samples, float normalizedTime)
    {
        if (samples == null || samples.Length < 2) return Mathf.Clamp01(normalizedTime);
        float position = Mathf.Clamp01(normalizedTime) * (samples.Length - 1);
        int from = Mathf.Min(Mathf.FloorToInt(position), samples.Length - 2);
        return Mathf.Lerp(samples[from], samples[from + 1], position - from);
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        acceleration = Mathf.Max(0f, acceleration);
        deceleration = Mathf.Max(0f, deceleration);
        rollDurationTicks = Mathf.Max(1, rollDurationTicks);
        rollCooldownTicks = Mathf.Max(0, rollCooldownTicks);
        normalHitStunTicks = Mathf.Max(1, normalHitStunTicks);
        heavyHitTicks = Mathf.Max(1, heavyHitTicks);
        if (rollDistanceSamples == null || rollDistanceSamples.Length < 2)
            rollDistanceSamples = new[] { 0f, 1f };
        rollDistanceSamples[0] = 0f;
        rollDistanceSamples[rollDistanceSamples.Length - 1] = 1f;
        for (int i = 1; i < rollDistanceSamples.Length; i++)
            rollDistanceSamples[i] = Mathf.Clamp(rollDistanceSamples[i], rollDistanceSamples[i - 1], 1f);
    }
}
