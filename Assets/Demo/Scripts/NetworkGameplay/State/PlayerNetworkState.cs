using UnityEngine;

public sealed class PlayerNetworkState
{
    public int EntityId;
    public int OwnerPlayerId;
    public Vector3 Position;
    public float VerticalVelocity;
    public bool Grounded;
    public float RotationY;
    public float CurrentHealth;
    public float MoveSpeed;
    public byte AnimationState;
    public uint LastProcessedInputSequence;
    public PlayerActionState Action;
    public float MaxHealth = 100f;
    public float Shield;
    public float ShieldCapacity;
    public float Skill1Cooldown;
    public float Skill2Cooldown;
    public bool IsFiring;
}
