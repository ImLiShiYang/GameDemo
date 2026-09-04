#if NETWORK_PLAYER_MIGRATION_CHECKS
using System;
using System.IO;
using UnityEngine;

// Pure checks against the production simulation and protocol. No scene or Unity native calls required.
public static class NetworkPlayerMigrationChecks
{
    private static int checks;

    public static int Main()
    {
        try
        {
            CheckRoll();
            CheckReplay();
            CheckRestrictions();
            CheckProtocol();
            Console.WriteLine("PASS: " + checks + " network player migration checks");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void CheckRoll()
    {
        Vector3 position = Vector3.zero;
        float rotation = 0f;
        PlayerActionState action = default;
        for (int tick = 0; tick < PlayerMovementSimulation.RollDurationTicks; tick++)
        {
            PlayerMovementSimulation.Step(ref position, ref rotation, ref action, Vector2.right, Vector2.up,
                tick == 0 ? ClientInputButtons.Roll : ClientInputButtons.None, true);
            Assert(action.IsRolling && action.IsInvincible, "Roll/invulnerability must remain active for the configured ticks");
        }
        Near(position.x, PlayerMovementSimulation.RollDistance, "Roll distance");
        Near(position.z, 0f, "Movement input takes priority over aim for roll direction");
        Near(rotation, 90f, "Roll heading");
        PlayerMovementSimulation.Step(ref position, ref rotation, ref action, Vector2.zero, Vector2.up, ClientInputButtons.None, true);
        Assert(!action.IsRolling && !action.IsInvincible, "Roll ends without an animation event");
        Vector3 stopped = position;
        for (int i = 0; i < 30; i++)
            PlayerMovementSimulation.Step(ref position, ref rotation, ref action, Vector2.zero, Vector2.up, ClientInputButtons.None, true);
        Near((position - stopped).magnitude, 0f, "A consumed roll press is not repeated");

        action = default;
        position = Vector3.zero;
        PlayerMovementSimulation.Step(ref position, ref rotation, ref action, Vector2.zero, Vector2.up, ClientInputButtons.Roll, true);
        Assert(action.RollDirection == Vector2.up, "Stationary roll uses aim");
        action = default;
        position = Vector3.zero;
        PlayerMovementSimulation.Step(ref position, ref rotation, ref action, Vector2.one, Vector2.up, ClientInputButtons.None, true, PlayerMovementSimulation.MoveSpeed, 100f);
        Near(position.magnitude, PlayerMovementSimulation.MoveSpeed * PlayerMovementSimulation.TickDeltaTime, "Diagonal speed is clamped");
    }

    private static void CheckReplay()
    {
        Vector3 predicted = Vector3.zero;
        float rotation = 0f;
        PlayerActionState action = default;
        Vector3 confirmedPosition = default;
        float confirmedRotation = 0f;
        PlayerActionState confirmedAction = default;
        for (int sequence = 1; sequence <= 24; sequence++)
        {
            PlayerMovementSimulation.Step(ref predicted, ref rotation, ref action, Vector2.right, Vector2.up,
                sequence == 3 ? ClientInputButtons.Roll : ClientInputButtons.None, true);
            if (sequence == 8)
            {
                confirmedPosition = predicted;
                confirmedRotation = rotation;
                confirmedAction = action;
            }
        }
        for (int sequence = 9; sequence <= 24; sequence++)
            PlayerMovementSimulation.Step(ref confirmedPosition, ref confirmedRotation, ref confirmedAction, Vector2.right, Vector2.up, ClientInputButtons.None, true);
        Near((confirmedPosition - predicted).magnitude, 0f, "Restore mid-roll and replay unacknowledged inputs");
        Near(confirmedRotation, rotation, "Replayed rotation");
        Assert(confirmedAction.RollTicks == action.RollTicks && confirmedAction.RollCooldownTicks == action.RollCooldownTicks,
            "Replayed action timers");
    }

    private static void CheckRestrictions()
    {
        Vector3 position = Vector3.zero;
        float rotation = 0f;
        PlayerActionState action = default;
        PlayerMovementSimulation.Step(ref position, ref rotation, ref action, Vector2.up, Vector2.up, ClientInputButtons.Roll, false);
        Assert(!action.IsRolling && position == Vector3.zero, "Dead/disabled battle state cannot roll or move");
        action.HitStunTicks = 8;
        PlayerMovementSimulation.Step(ref position, ref rotation, ref action, Vector2.up, Vector2.up, ClientInputButtons.Roll, true);
        Assert(!action.IsRolling && position == Vector3.zero && action.HitStunTicks == 7, "Hit stun blocks input and advances deterministically");
        action = new PlayerActionState { RollCooldownTicks = 5 };
        PlayerMovementSimulation.Step(ref position, ref rotation, ref action, Vector2.zero, Vector2.up, ClientInputButtons.Roll, true);
        Assert(!action.IsRolling, "Roll cooldown is enforced");
    }

    private static void CheckProtocol()
    {
        ClientInputMessage input = new ClientInputMessage
        {
            Sequence = 12, ClientTick = 12, Horizontal = 0.5f, Vertical = -1f, AimX = 1f,
            Buttons = ClientInputButtons.Roll | ClientInputButtons.Skill1 | ClientInputButtons.Skill2 | ClientInputButtons.Fire
        };
        ClientInputMessage decodedInput = NetworkProtocol.DeserializeClientInput(NetworkProtocol.Serialize(input));
        Assert(decodedInput.Buttons == input.Buttons && decodedInput.Sequence == 12, "Both skills and roll survive input serialization");
        WorldSnapshotMessage snapshot = new WorldSnapshotMessage { ServerTick = 42 };
        PlayerNetworkState player = new PlayerNetworkState
        {
            EntityId = 1001, OwnerPlayerId = 1, Position = new Vector3(2f, 0f, 5f), RotationY = 90f,
            CurrentHealth = 80f, MaxHealth = 100f, Shield = 10f, ShieldCapacity = 25f,
            Skill1Cooldown = 4.5f, Skill2Cooldown = 2f, IsFiring = true, LastProcessedInputSequence = 12,
            Action = new PlayerActionState { RollTicks = 10, RollCooldownTicks = 3, HitStunTicks = 0, RollDirection = Vector2.right, MoveDirection = Vector2.up }
        };
        snapshot.Players.Add(player);
        PlayerNetworkState decoded = NetworkProtocol.DeserializeWorldSnapshot(NetworkProtocol.Serialize(snapshot)).Players[0];
        Assert(decoded.Action.RollTicks == 10 && decoded.Action.RollDirection == Vector2.right && decoded.Action.MoveDirection == Vector2.up,
            "Snapshot includes complete replay action state");
        Assert(decoded.Shield == 10f && decoded.MaxHealth == 100f && decoded.IsFiring && decoded.Skill1Cooldown == 4.5f && decoded.Skill2Cooldown == 2f,
            "Snapshot preserves health, firing and independent cooldowns");
        BattleEventMessage skill = new BattleEventMessage
        {
            EventType = BattleEventType.PlayerSkillCast, SourceEntityId = 1002, SkillSlot = 2,
            Position = new Vector3(1f, 2f, 3f), Direction = Vector3.forward, Range = 12f, Duration = 0f
        };
        BattleEventMessage decodedSkill = NetworkProtocol.DeserializeBattleEvent(NetworkProtocol.Serialize(skill));
        Assert(decodedSkill.SkillSlot == 2 && decodedSkill.Direction == Vector3.forward && decodedSkill.Range == 12f,
            "Skill effect uses the server origin, direction and range");
        player.Action.RollTicks = -1;
        Reject(() => NetworkProtocol.Serialize(snapshot), "Negative roll timer rejected");
        player.Action.RollTicks = 0;
        player.Skill1Cooldown = float.NaN;
        Reject(() => NetworkProtocol.Serialize(snapshot), "Non-finite cooldown rejected");
        skill.SkillSlot = 3;
        Reject(() => NetworkProtocol.Serialize(skill), "Unknown skill slot rejected");
        Assert(NetworkPacketHeader.CurrentProtocolVersion == 6, "Protocol version bumped for incompatible payload changes");
    }

    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { checks++; return; }
        throw new Exception(message);
    }

    private static void Near(float value, float expected, string message)
    {
        Assert(Math.Abs(value - expected) < 0.0001f, message + ": " + value + " expected " + expected);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
    }
}
#endif
