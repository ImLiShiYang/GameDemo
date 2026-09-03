using UnityEngine;

public sealed class PlayerNetworkState
{
    public int EntityId;
    public int OwnerPlayerId;
    public Vector3 Position;
    public float RotationY;
    public float CurrentHealth;
    public float MoveSpeed;
    public byte AnimationState;
    public uint LastProcessedInputSequence;
}
