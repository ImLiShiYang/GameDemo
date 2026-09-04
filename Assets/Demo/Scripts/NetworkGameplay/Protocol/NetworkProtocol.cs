using System;
using System.IO;
using System.Text;
using UnityEngine;

public static class NetworkProtocol
{
    private const int MaximumStringBytes = 256;
    private const int MaximumSnapshotEntityCount = 2048;

    public static byte[] Serialize(ConnectRequestMessage message)
    {
        return WritePayload(writer =>
        {
            writer.Write(message.ProtocolVersion);
            writer.Write(message.PlayerId);
            WriteString(writer, message.ClientBuildVersion);
            WriteString(writer, message.PlayerName);
        });
    }

    public static ConnectRequestMessage DeserializeConnectRequest(byte[] payload)
    {
        return ReadPayload(payload, reader => new ConnectRequestMessage
        {
            ProtocolVersion = reader.ReadUInt16(),
            PlayerId = reader.ReadInt32(),
            ClientBuildVersion = ReadString(reader),
            PlayerName = ReadString(reader)
        });
    }

    public static byte[] Serialize(WelcomeMessage message)
    {
        return WritePayload(writer =>
        {
            writer.Write(message.MatchId);
            writer.Write(message.AssignedPlayerId);
            writer.Write(message.PlayerEntityId);
            writer.Write(message.ServerTick);
            writer.Write(message.TickRate);
            writer.Write(message.SnapshotRate);
        });
    }

    public static WelcomeMessage DeserializeWelcome(byte[] payload)
    {
        return ReadPayload(payload, reader => new WelcomeMessage
        {
            MatchId = reader.ReadInt64(),
            AssignedPlayerId = reader.ReadInt32(),
            PlayerEntityId = reader.ReadInt32(),
            ServerTick = reader.ReadUInt32(),
            TickRate = reader.ReadInt32(),
            SnapshotRate = reader.ReadInt32()
        });
    }

    public static byte[] Serialize(ConnectionRejectedMessage message)
    {
        return WritePayload(writer => WriteString(writer, message.Reason));
    }

    public static ConnectionRejectedMessage DeserializeConnectionRejected(byte[] payload)
    {
        return ReadPayload(payload, reader => new ConnectionRejectedMessage { Reason = ReadString(reader) });
    }

    public static byte[] Serialize(ClientInputMessage message)
    {
        return WritePayload(writer =>
        {
            writer.Write(message.Sequence);
            writer.Write(message.ClientTick);
            writer.Write(message.Horizontal);
            writer.Write(message.Vertical);
            writer.Write(message.AimX);
            writer.Write(message.AimZ);
            writer.Write((byte)message.Buttons);
        });
    }

    public static ClientInputMessage DeserializeClientInput(byte[] payload)
    {
        ClientInputMessage message = ReadPayload(payload, reader => new ClientInputMessage
        {
            Sequence = reader.ReadUInt32(),
            ClientTick = reader.ReadUInt32(),
            Horizontal = reader.ReadSingle(),
            Vertical = reader.ReadSingle(),
            AimX = reader.ReadSingle(),
            AimZ = reader.ReadSingle(),
            Buttons = (ClientInputButtons)reader.ReadByte()
        });
        ValidateFinite(message.Horizontal, nameof(message.Horizontal));
        ValidateFinite(message.Vertical, nameof(message.Vertical));
        ValidateFinite(message.AimX, nameof(message.AimX));
        ValidateFinite(message.AimZ, nameof(message.AimZ));
        return message;
    }

    public static byte[] Serialize(EntitySpawnMessage message)
    {
        ValidateEntitySpawn(message);
        return WritePayload(writer =>
        {
            writer.Write(message.EntityId);
            writer.Write((byte)message.EntityType);
            writer.Write(message.PrefabId);
            writer.Write(message.OwnerPlayerId);
            writer.Write(message.Position.x);
            writer.Write(message.Position.y);
            writer.Write(message.Position.z);
            writer.Write(message.Rotation.x);
            writer.Write(message.Rotation.y);
            writer.Write(message.Rotation.z);
            writer.Write(message.Rotation.w);
            writer.Write(message.Velocity.x);
            writer.Write(message.Velocity.y);
            writer.Write(message.Velocity.z);
            writer.Write(message.SpawnTick);
            writer.Write(message.CurrentHealth);
            writer.Write(message.MaxHealth);
        });
    }

    public static EntitySpawnMessage DeserializeEntitySpawn(byte[] payload)
    {
        EntitySpawnMessage message = ReadPayload(payload, reader => new EntitySpawnMessage
        {
            EntityId = reader.ReadInt32(),
            EntityType = (NetworkEntityType)reader.ReadByte(),
            PrefabId = reader.ReadInt32(),
            OwnerPlayerId = reader.ReadInt32(),
            Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            Rotation = new Quaternion(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            Velocity = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            SpawnTick = reader.ReadUInt32(),
            CurrentHealth = reader.ReadSingle(),
            MaxHealth = reader.ReadSingle()
        });
        ValidateEntitySpawn(message);
        return message;
    }

    public static byte[] Serialize(EntityDespawnMessage message)
    {
        ValidateEntityId(message.EntityId);

        if (!Enum.IsDefined(typeof(EntityDespawnReason), message.Reason))
        {
            throw new InvalidDataException($"未知实体删除原因：{(byte)message.Reason}。");
        }

        return WritePayload(writer =>
        {
            writer.Write(message.EntityId);
            writer.Write((byte)message.Reason);
        });
    }

    public static EntityDespawnMessage DeserializeEntityDespawn(byte[] payload)
    {
        EntityDespawnMessage message = ReadPayload(payload, reader => new EntityDespawnMessage
        {
            EntityId = reader.ReadInt32(),
            Reason = (EntityDespawnReason)reader.ReadByte()
        });
        ValidateEntityId(message.EntityId);

        if (!Enum.IsDefined(typeof(EntityDespawnReason), message.Reason))
        {
            throw new InvalidDataException($"未知实体删除原因：{(byte)message.Reason}。");
        }

        return message;
    }

    public static byte[] Serialize(BattleEventMessage message)
    {
        ValidateBattleEvent(message);
        return WritePayload(writer =>
        {
            writer.Write((byte)message.EventType);
            writer.Write(message.SourceEntityId);
            writer.Write(message.TargetEntityId);
            writer.Write(message.Amount);
            writer.Write(message.CurrentHealth);
            writer.Write(message.MaxHealth);
            writer.Write(message.Position.x);
            writer.Write(message.Position.y);
            writer.Write(message.Position.z);
            writer.Write((byte)message.Phase);
            writer.Write(message.CurrentWave);
            writer.Write(message.SkillSlot);
            writer.Write(message.Direction.x);
            writer.Write(message.Direction.y);
            writer.Write(message.Direction.z);
            writer.Write(message.Range);
            writer.Write(message.Duration);
        });
    }

    public static BattleEventMessage DeserializeBattleEvent(byte[] payload)
    {
        BattleEventMessage message = ReadPayload(payload, reader => new BattleEventMessage
        {
            EventType = (BattleEventType)reader.ReadByte(),
            SourceEntityId = reader.ReadInt32(),
            TargetEntityId = reader.ReadInt32(),
            Amount = reader.ReadSingle(),
            CurrentHealth = reader.ReadSingle(),
            MaxHealth = reader.ReadSingle(),
            Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            Phase = (BattlePhase)reader.ReadByte(),
            CurrentWave = reader.ReadInt32(),
            SkillSlot = reader.ReadByte(),
            Direction = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
            Range = reader.ReadSingle(),
            Duration = reader.ReadSingle()
        });
        ValidateBattleEvent(message);
        return message;
    }

    public static byte[] Serialize(WorldSnapshotMessage message)
    {
        return WritePayload(writer =>
        {
            writer.Write(message.ServerTick);
            writer.Write((byte)message.Battle.Phase);
            writer.Write(message.Battle.CurrentWave);
            writer.Write(message.Battle.AliveEnemyCount);
            writer.Write(message.Battle.AllEnemiesSpawned);
            writer.Write(message.Battle.BossEntityId);
            writer.Write(message.Battle.ServerTick);
            writer.Write((byte)message.Players.Count);

            foreach (PlayerNetworkState player in message.Players)
            {
                ValidatePlayerState(player);
                writer.Write(player.EntityId);
                writer.Write(player.OwnerPlayerId);
                writer.Write(player.Position.x);
                writer.Write(player.Position.y);
                writer.Write(player.Position.z);
                writer.Write(player.RotationY);
                writer.Write(player.CurrentHealth);
                writer.Write(player.MoveSpeed);
                writer.Write(player.AnimationState);
                writer.Write(player.LastProcessedInputSequence);
                writer.Write(player.Action.RollTicks);
                writer.Write(player.Action.RollCooldownTicks);
                writer.Write(player.Action.HitStunTicks);
                writer.Write(player.Action.RollDirection.x);
                writer.Write(player.Action.RollDirection.y);
                writer.Write(player.Action.MoveDirection.x);
                writer.Write(player.Action.MoveDirection.y);
                writer.Write(player.MaxHealth);
                writer.Write(player.Shield);
                writer.Write(player.ShieldCapacity);
                writer.Write(player.Skill1Cooldown);
                writer.Write(player.Skill2Cooldown);
                writer.Write(player.IsFiring);
                writer.Write(player.VerticalVelocity);
                writer.Write(player.Grounded);
            }

            if (message.Entities.Count > MaximumSnapshotEntityCount)
            {
                throw new InvalidDataException($"实体状态数量 {message.Entities.Count} 超过协议限制。");
            }

            writer.Write((ushort)message.Entities.Count);

            foreach (EntityNetworkState entity in message.Entities)
            {
                ValidateEntityState(entity);
                writer.Write(entity.EntityId);
                writer.Write((byte)entity.EntityType);
                writer.Write(entity.PrefabId);
                writer.Write(entity.OwnerPlayerId);
                writer.Write(entity.Position.x);
                writer.Write(entity.Position.y);
                writer.Write(entity.Position.z);
                writer.Write(entity.Velocity.x);
                writer.Write(entity.Velocity.y);
                writer.Write(entity.Velocity.z);
                writer.Write(entity.RotationY);
                writer.Write(entity.CurrentHealth);
                writer.Write(entity.MaxHealth);
                writer.Write(entity.AnimationState);
                writer.Write(entity.TargetEntityId);
            }
        });
    }

    public static WorldSnapshotMessage DeserializeWorldSnapshot(byte[] payload)
    {
        return ReadPayload(payload, reader =>
        {
            WorldSnapshotMessage message = new WorldSnapshotMessage { ServerTick = reader.ReadUInt32() };
            message.Battle.Phase = (BattlePhase)reader.ReadByte();
            message.Battle.CurrentWave = reader.ReadInt32();
            message.Battle.AliveEnemyCount = reader.ReadInt32();
            message.Battle.AllEnemiesSpawned = reader.ReadBoolean();
            message.Battle.BossEntityId = reader.ReadInt32();
            message.Battle.ServerTick = reader.ReadUInt32();
            ValidateBattleState(message.Battle);
            int playerCount = reader.ReadByte();

            if (playerCount > 2)
            {
                throw new InvalidDataException($"玩家状态数量 {playerCount} 超过协议限制。");
            }

            for (int i = 0; i < playerCount; i++)
            {
                PlayerNetworkState player = new PlayerNetworkState
                {
                    EntityId = reader.ReadInt32(),
                    OwnerPlayerId = reader.ReadInt32(),
                    Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    RotationY = reader.ReadSingle(),
                    CurrentHealth = reader.ReadSingle(),
                    MoveSpeed = reader.ReadSingle(),
                    AnimationState = reader.ReadByte(),
                    LastProcessedInputSequence = reader.ReadUInt32(),
                    Action = new PlayerActionState
                    {
                        RollTicks = reader.ReadInt32(),
                        RollCooldownTicks = reader.ReadInt32(),
                        HitStunTicks = reader.ReadInt32(),
                        RollDirection = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                        MoveDirection = new Vector2(reader.ReadSingle(), reader.ReadSingle())
                    },
                    MaxHealth = reader.ReadSingle(),
                    Shield = reader.ReadSingle(),
                    ShieldCapacity = reader.ReadSingle(),
                    Skill1Cooldown = reader.ReadSingle(),
                    Skill2Cooldown = reader.ReadSingle(),
                    IsFiring = reader.ReadBoolean(),
                    VerticalVelocity = reader.ReadSingle(),
                    Grounded = reader.ReadBoolean()
                };
                ValidatePlayerState(player);
                message.Players.Add(player);
            }

            int entityCount = reader.ReadUInt16();

            if (entityCount > MaximumSnapshotEntityCount)
            {
                throw new InvalidDataException($"实体状态数量 {entityCount} 超过协议限制。");
            }

            for (int i = 0; i < entityCount; i++)
            {
                EntityNetworkState entity = new EntityNetworkState
                {
                    EntityId = reader.ReadInt32(),
                    EntityType = (NetworkEntityType)reader.ReadByte(),
                    PrefabId = reader.ReadInt32(),
                    OwnerPlayerId = reader.ReadInt32(),
                    Position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    Velocity = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                    RotationY = reader.ReadSingle(),
                    CurrentHealth = reader.ReadSingle(),
                    MaxHealth = reader.ReadSingle(),
                    AnimationState = reader.ReadByte(),
                    TargetEntityId = reader.ReadInt32()
                };
                ValidateEntityState(entity);
                message.Entities.Add(entity);
            }

            return message;
        });
    }

    private static byte[] WritePayload(Action<BinaryWriter> write)
    {
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8))
        {
            write(writer);
            writer.Flush();
            return stream.ToArray();
        }
    }

    private static T ReadPayload<T>(byte[] payload, Func<BinaryReader, T> read)
    {
        if (payload == null)
        {
            throw new InvalidDataException("消息 Payload 不能为空。");
        }

        using (MemoryStream stream = new MemoryStream(payload, false))
        using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8))
        {
            T result = read(reader);

            if (stream.Position != stream.Length)
            {
                throw new InvalidDataException("消息 Payload 包含未读取的尾部数据。");
            }

            return result;
        }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);

        if (bytes.Length > MaximumStringBytes)
        {
            throw new InvalidDataException($"字符串超过 {MaximumStringBytes} 字节限制。");
        }

        writer.Write((ushort)bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        int length = reader.ReadUInt16();

        if (length > MaximumStringBytes)
        {
            throw new InvalidDataException($"字符串长度 {length} 超过协议限制。");
        }

        byte[] bytes = reader.ReadBytes(length);

        if (bytes.Length != length)
        {
            throw new EndOfStreamException("字符串数据不完整。");
        }

        return Encoding.UTF8.GetString(bytes);
    }

    private static void ValidatePlayerState(PlayerNetworkState player)
    {
        if (player.EntityId <= 0 || player.OwnerPlayerId < 1 || player.OwnerPlayerId > 2)
        {
            throw new InvalidDataException("玩家快照包含非法 EntityId 或 OwnerPlayerId。");
        }

        ValidateFinite(player.Position.x, "Position.x");
        ValidateFinite(player.Position.y, "Position.y");
        ValidateFinite(player.Position.z, "Position.z");
        ValidateFinite(player.RotationY, nameof(player.RotationY));
        ValidateFinite(player.CurrentHealth, nameof(player.CurrentHealth));
        ValidateFinite(player.MoveSpeed, nameof(player.MoveSpeed));
        ValidateFinite(player.VerticalVelocity, nameof(player.VerticalVelocity));
        ValidateFinite(player.MaxHealth, nameof(player.MaxHealth));
        ValidateFinite(player.Shield, nameof(player.Shield));
        ValidateFinite(player.ShieldCapacity, nameof(player.ShieldCapacity));
        ValidateFinite(player.Skill1Cooldown, nameof(player.Skill1Cooldown));
        ValidateFinite(player.Skill2Cooldown, nameof(player.Skill2Cooldown));
        ValidateFinite(player.Action.RollDirection.x, "RollDirection.x");
        ValidateFinite(player.Action.RollDirection.y, "RollDirection.y");
        ValidateFinite(player.Action.MoveDirection.x, "MoveDirection.x");
        ValidateFinite(player.Action.MoveDirection.y, "MoveDirection.y");
        if (player.Action.RollTicks < 0 || player.Action.RollTicks > PlayerMovementSimulation.RollDurationTicks ||
            player.Action.RollCooldownTicks < 0 || player.Action.HitStunTicks < 0 || player.MaxHealth <= 0f ||
            player.CurrentHealth < 0f || player.CurrentHealth > player.MaxHealth || player.Shield < 0f ||
            player.Shield > player.ShieldCapacity || player.Skill1Cooldown < 0f || player.Skill2Cooldown < 0f)
        {
            throw new InvalidDataException("玩家动作或生命状态无效。");
        }
    }

    private static void ValidateEntitySpawn(EntitySpawnMessage message)
    {
        ValidateEntityIdentity(message.EntityId, message.EntityType, message.PrefabId, message.OwnerPlayerId);
        ValidateFinite(message.Position.x, "Position.x");
        ValidateFinite(message.Position.y, "Position.y");
        ValidateFinite(message.Position.z, "Position.z");
        ValidateFinite(message.Rotation.x, "Rotation.x");
        ValidateFinite(message.Rotation.y, "Rotation.y");
        ValidateFinite(message.Rotation.z, "Rotation.z");
        ValidateFinite(message.Rotation.w, "Rotation.w");
        ValidateFinite(message.Velocity.x, "Velocity.x");
        ValidateFinite(message.Velocity.y, "Velocity.y");
        ValidateFinite(message.Velocity.z, "Velocity.z");
        ValidateFinite(message.CurrentHealth, nameof(message.CurrentHealth));
        ValidateFinite(message.MaxHealth, nameof(message.MaxHealth));

        if (message.MaxHealth < 0f || message.CurrentHealth < 0f || message.CurrentHealth > message.MaxHealth)
        {
            throw new InvalidDataException("实体 Spawn 包含非法生命值。");
        }
    }

    private static void ValidateEntityState(EntityNetworkState entity)
    {
        ValidateEntityIdentity(entity.EntityId, entity.EntityType, entity.PrefabId, entity.OwnerPlayerId);
        ValidateFinite(entity.Position.x, "Position.x");
        ValidateFinite(entity.Position.y, "Position.y");
        ValidateFinite(entity.Position.z, "Position.z");
        ValidateFinite(entity.Velocity.x, "Velocity.x");
        ValidateFinite(entity.Velocity.y, "Velocity.y");
        ValidateFinite(entity.Velocity.z, "Velocity.z");
        ValidateFinite(entity.RotationY, nameof(entity.RotationY));
        ValidateFinite(entity.CurrentHealth, nameof(entity.CurrentHealth));
        ValidateFinite(entity.MaxHealth, nameof(entity.MaxHealth));

        if (entity.MaxHealth < 0f || entity.CurrentHealth < 0f || entity.CurrentHealth > entity.MaxHealth)
        {
            throw new InvalidDataException("实体快照包含非法生命值。");
        }

        if (entity.TargetEntityId < 0)
        {
            throw new InvalidDataException("实体快照包含非法 TargetEntityId。");
        }
    }

    private static void ValidateBattleEvent(BattleEventMessage message)
    {
        if (message == null || !Enum.IsDefined(typeof(BattleEventType), message.EventType))
        {
            throw new InvalidDataException("战斗事件类型无效。");
        }

        bool entityEvent = message.EventType == BattleEventType.Damage || message.EventType == BattleEventType.EntityDied ||
            message.EventType == BattleEventType.BossSpawned || message.EventType == BattleEventType.BossDied;

        if (entityEvent)
        {
            ValidateEntityId(message.TargetEntityId);
        }

        if (message.EventType == BattleEventType.PlayerFired)
        {
            ValidateEntityId(message.SourceEntityId);
            ValidateEntityId(message.TargetEntityId);
        }

        if (message.SourceEntityId < 0 || message.TargetEntityId < 0)
        {
            throw new InvalidDataException("战斗事件包含非法 EntityId。");
        }
        ValidateFinite(message.Amount, nameof(message.Amount));
        ValidateFinite(message.Direction.x, "Direction.x");
        ValidateFinite(message.Direction.y, "Direction.y");
        ValidateFinite(message.Direction.z, "Direction.z");
        ValidateFinite(message.Range, nameof(message.Range));
        ValidateFinite(message.Duration, nameof(message.Duration));
        if (message.EventType == BattleEventType.PlayerSkillCast &&
            (message.SourceEntityId <= 0 || message.SkillSlot < 1 || message.SkillSlot > 2 || message.Range <= 0f || message.Duration < 0f))
        {
            throw new InvalidDataException("玩家技能事件无效。");
        }
        ValidateFinite(message.CurrentHealth, nameof(message.CurrentHealth));
        ValidateFinite(message.MaxHealth, nameof(message.MaxHealth));
        ValidateFinite(message.Position.x, "Position.x");
        ValidateFinite(message.Position.y, "Position.y");
        ValidateFinite(message.Position.z, "Position.z");

        if (message.Amount < 0f || message.MaxHealth < 0f || message.CurrentHealth < 0f ||
            message.CurrentHealth > message.MaxHealth)
        {
            throw new InvalidDataException("战斗事件包含非法伤害或生命值。");
        }

        if (!Enum.IsDefined(typeof(BattlePhase), message.Phase) || message.CurrentWave < 0)
        {
            throw new InvalidDataException("战斗事件包含非法阶段或波次。");
        }
    }

    private static void ValidateBattleState(BattleNetworkState state)
    {
        if (state == null || !Enum.IsDefined(typeof(BattlePhase), state.Phase) || state.CurrentWave < 0 ||
            state.AliveEnemyCount < 0 || state.BossEntityId < 0)
        {
            throw new InvalidDataException("世界快照包含非法战斗状态。");
        }
    }

    private static void ValidateEntityIdentity(int entityId, NetworkEntityType entityType, int prefabId, int ownerPlayerId)
    {
        ValidateEntityId(entityId);

        if (!Enum.IsDefined(typeof(NetworkEntityType), entityType))
        {
            throw new InvalidDataException($"未知网络实体类型：{(byte)entityType}。");
        }

        if (prefabId <= 0 || ownerPlayerId < 0 || ownerPlayerId > 2)
        {
            throw new InvalidDataException("网络实体包含非法 PrefabId 或 OwnerPlayerId。");
        }
    }

    private static void ValidateEntityId(int entityId)
    {
        if (entityId <= 0)
        {
            throw new InvalidDataException($"非法 EntityId：{entityId}。");
        }
    }

    private static void ValidateFinite(float value, string fieldName)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            throw new InvalidDataException($"字段 {fieldName} 不是有限数值。");
        }
    }
}
