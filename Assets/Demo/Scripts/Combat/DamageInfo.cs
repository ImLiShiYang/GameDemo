using UnityEngine;

public enum DamageKind
{
    Normal,
    Skill
}

public readonly struct DamageInfo
{
    public readonly float Amount;
    public readonly GameObject Source;
    public readonly Vector3 HitPoint;
    public readonly Vector3 HitDirection;
    public readonly Vector3 HitNormal;
    public readonly DamageKind Kind;
    public readonly int InterruptPower;

    public DamageInfo(
        float amount,
        GameObject source,
        Vector3 hitPoint,
        Vector3 hitDirection,
        Vector3 hitNormal,
        DamageKind kind = DamageKind.Normal,
        int interruptPower = 0)
    {
        Amount = amount;
        Source = source;
        HitPoint = hitPoint;
        HitDirection = hitDirection;
        HitNormal = hitNormal;
        Kind = kind;
        InterruptPower = Mathf.Max(0, interruptPower);
    }
}
