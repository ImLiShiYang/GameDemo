using UnityEngine;

public class PlayerCombatStats : MonoBehaviour
{
    public float FireIntervalMultiplier { get; private set; } = 1f;

    public int PierceCount { get; private set; } = 0;

    public int ProjectileCount { get; private set; } = 1;

    public float SpreadAngle { get; private set; } = 0f;

    /// <summary>
    /// 根据 UpgradeEffect 的配置，修改玩家对应的战斗属性。
    /// stat 决定修改哪个属性，operation 决定加法还是乘法，value 是修改值。
    /// </summary>
    public void ApplyEffect(UpgradeEffect effect)
    {
        switch (effect.stat)
        {
            // 快速射击：修改射击间隔倍率，例如 1 × 0.85 = 0.85。
            case UpgradeStat.FireIntervalMultiplier:
                FireIntervalMultiplier =
                    ApplyFloat(
                        FireIntervalMultiplier,
                        effect.operation,
                        effect.value
                    );

                // 限制最低倍率为 0.1，防止射击间隔过小。
                FireIntervalMultiplier =
                    Mathf.Max(0.1f, FireIntervalMultiplier);

                break;

            // 穿透弹药：修改额外穿透次数，例如 0 + 1 = 1。
            case UpgradeStat.PierceCount:
                PierceCount =
                    Mathf.Max(
                        0,
                        ApplyInt(
                            PierceCount,
                            effect.operation,
                            effect.value
                        )
                    );

                break;

            // 扩散射击：修改一次射击生成的子弹数量，例如 1 + 2 = 3。
            case UpgradeStat.ProjectileCount:
                ProjectileCount =
                    Mathf.Max(
                        1,
                        ApplyInt(
                            ProjectileCount,
                            effect.operation,
                            effect.value
                        )
                    );

                break;

            // 扩散射击：修改子弹总扩散角度，例如 0 + 20 = 20°。
            case UpgradeStat.SpreadAngle:
                SpreadAngle =
                    Mathf.Max(
                        0f,
                        ApplyFloat(
                            SpreadAngle,
                            effect.operation,
                            effect.value
                        )
                    );

                break;
        }
    }

    private static float ApplyFloat(float currentValue,UpgradeOperation operation,float value)
    {
        switch (operation)
        {
            case UpgradeOperation.Add:
                return currentValue + value;

            case UpgradeOperation.Multiply:
                return currentValue * value;

            default:
                return currentValue;
        }
    }

    private static int ApplyInt(
        int currentValue,
        UpgradeOperation operation,
        float value)
    {
        return Mathf.RoundToInt(
            ApplyFloat(
                currentValue,
                operation,
                value
            )
        );
    }
}