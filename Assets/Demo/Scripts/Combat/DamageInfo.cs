using UnityEngine;

public readonly struct DamageInfo
{
    public readonly float Amount;
    public readonly GameObject Source;
    public readonly Vector3 HitPoint;
    public readonly Vector3 HitDirection;

    public DamageInfo(
        float amount,
        GameObject source,
        Vector3 hitPoint,
        Vector3 hitDirection)
    {
        Amount = amount;
        Source = source;
        HitPoint = hitPoint;
        HitDirection = hitDirection;
    }
}