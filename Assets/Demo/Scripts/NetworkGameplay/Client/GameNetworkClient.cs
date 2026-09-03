using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public sealed class GameNetworkClient : MonoBehaviour
{
    private readonly ConcurrentQueue<ClientEvent> events = new ConcurrentQueue<ClientEvent>();
    private readonly object sendLock = new object();
    private readonly Dictionary<int, KnownEntitySpawn> knownEntitySpawns = new Dictionary<int, KnownEntitySpawn>();
    private readonly Dictionary<int, uint> despawnedEntityTicks = new Dictionary<int, uint>();

    private TcpClient tcpClient;
    private NetworkStream stream;
    private Thread networkThread;
    private volatile bool running;
    private uint sendSequence;

    public string StatusText { get; private set; } = "未连接";
    public bool IsWelcomed { get; private set; }
    public WelcomeMessage LastWelcome { get; private set; }
    public uint LastSnapshotTick { get; private set; }

    public event Action<WelcomeMessage> Welcomed;
    public event Action<string> ConnectionFailed;
    public event Action<WorldSnapshotMessage> SnapshotReceived;
    public event Action<EntitySpawnMessage, uint> EntitySpawnReceived;
    public event Action<EntityDespawnMessage, uint> EntityDespawnReceived;
    public event Action<BattleEventMessage, uint> BattleEventReceived;

    public void Connect(string address, int port, int requestedPlayerId, string playerName, string buildVersion)
    {
        if (running)
        {
            return;
        }

        running = true;
        StatusText = $"正在连接 {address}:{port}";
        networkThread = new Thread(() => NetworkLoop(address, port, requestedPlayerId, playerName, buildVersion))
        {
            IsBackground = true,
            Name = "GameNetworkClient"
        };
        networkThread.Start();
    }

    public void Disconnect()
    {
        if (!running && tcpClient == null)
        {
            return;
        }

        running = false;
        IsWelcomed = false;
        LastWelcome = null;
        LastSnapshotTick = 0;
        knownEntitySpawns.Clear();
        despawnedEntityTicks.Clear();

        try
        {
            tcpClient?.Close();
        }
        catch (SocketException)
        {
        }

        tcpClient = null;
        stream = null;
    }

    public bool SendClientInput(ClientInputMessage input)
    {
        if (!running || !IsWelcomed || stream == null)
        {
            return false;
        }

        try
        {
            Send(NetworkMessageType.ClientInput, NetworkProtocol.Serialize(input), NetworkRuntime.ServerTick, NetworkRuntime.MatchId);
            return true;
        }
        catch (Exception exception)
        {
            NetworkLog.Error($"发送客户端输入失败：{exception.Message}");
            Disconnect();
            return false;
        }
    }

    public void ReplayKnownEntitySpawns(Action<EntitySpawnMessage, uint> receiver)
    {
        if (receiver == null)
        {
            return;
        }

        foreach (KnownEntitySpawn knownSpawn in knownEntitySpawns.Values)
        {
            receiver(knownSpawn.Message, knownSpawn.ServerTick);
        }
    }

    private void Update()
    {
        while (events.TryDequeue(out ClientEvent clientEvent))
        {
            switch (clientEvent.Type)
            {
                case ClientEventType.TcpConnected:
                    StatusText = "TCP 已连接，等待服务器验证";
                    NetworkLog.Info(StatusText);
                    break;
                case ClientEventType.Packet:
                    HandlePacket(clientEvent.Packet);
                    break;
                case ClientEventType.Disconnected:
                    IsWelcomed = false;

                    if (StatusText.StartsWith("服务器拒绝：", StringComparison.Ordinal))
                    {
                        NetworkLog.Info("服务器已关闭被拒绝的连接。");
                        break;
                    }

                    StatusText = string.IsNullOrEmpty(clientEvent.Error) ? "已断开" : $"连接失败：{clientEvent.Error}";

                    if (string.IsNullOrEmpty(clientEvent.Error))
                    {
                        NetworkLog.Info(StatusText);
                    }
                    else
                    {
                        NetworkLog.Error(StatusText);
                    }

                    ConnectionFailed?.Invoke(StatusText);
                    break;
            }
        }
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    private void NetworkLoop(string address, int port, int requestedPlayerId, string playerName, string buildVersion)
    {
        string error = null;

        try
        {
            tcpClient = new TcpClient { NoDelay = true };
            tcpClient.Connect(address, port);
            stream = tcpClient.GetStream();
            events.Enqueue(ClientEvent.Connected());
            ConnectRequestMessage request = new ConnectRequestMessage
            {
                ProtocolVersion = NetworkPacketHeader.CurrentProtocolVersion,
                PlayerId = requestedPlayerId,
                ClientBuildVersion = buildVersion,
                PlayerName = playerName
            };
            Send(NetworkMessageType.ConnectRequest, NetworkProtocol.Serialize(request), 0, 0);

            while (running)
            {
                events.Enqueue(ClientEvent.Received(NetworkPacketCodec.ReadPacket(stream)));
            }
        }
        catch (EndOfStreamException)
        {
        }
        catch (IOException exception)
        {
            if (running)
            {
                error = exception.Message;
            }
        }
        catch (Exception exception)
        {
            if (running)
            {
                error = exception.Message;
            }
        }
        finally
        {
            running = false;

            try
            {
                tcpClient?.Close();
            }
            catch (SocketException)
            {
            }

            events.Enqueue(ClientEvent.Disconnected(error));
        }
    }

    private void HandlePacket(NetworkPacket packet)
    {
        try
        {
            switch (packet.Header.MessageType)
            {
                case NetworkMessageType.Welcome:
                    WelcomeMessage welcome = NetworkProtocol.DeserializeWelcome(packet.Payload);

                    if (welcome.AssignedPlayerId < 1 || welcome.AssignedPlayerId > 2 || welcome.PlayerEntityId <= 0)
                    {
                        throw new InvalidDataException("Welcome 包含非法玩家身份。");
                    }

                    knownEntitySpawns.Clear();
                    despawnedEntityTicks.Clear();
                    NetworkRuntime.LocalPlayerId = welcome.AssignedPlayerId;
                    NetworkRuntime.LocalPlayerEntityId = welcome.PlayerEntityId;
                    NetworkRuntime.MatchId = welcome.MatchId;
                    NetworkRuntime.ServerTick = welcome.ServerTick;
                    LastSnapshotTick = 0;
                    IsWelcomed = true;
                    LastWelcome = welcome;
                    StatusText = $"已连接（Player {welcome.AssignedPlayerId}）";
                    NetworkLog.Info($"收到 Welcome：PlayerId {welcome.AssignedPlayerId}，EntityId {welcome.PlayerEntityId}，TickRate {welcome.TickRate}。");
                    Welcomed?.Invoke(welcome);
                    break;
                case NetworkMessageType.WorldSnapshot:
                    if (!IsWelcomed || packet.Header.MatchId != NetworkRuntime.MatchId)
                    {
                        NetworkLog.Warning("忽略 MatchId 不匹配或验证前到达的世界快照。");
                        break;
                    }

                    WorldSnapshotMessage snapshot = NetworkProtocol.DeserializeWorldSnapshot(packet.Payload);

                    if (snapshot.ServerTick <= LastSnapshotTick)
                    {
                        break;
                    }

                    LastSnapshotTick = snapshot.ServerTick;
                    NetworkRuntime.ServerTick = snapshot.ServerTick;
                    SnapshotReceived?.Invoke(snapshot);
                    break;
                case NetworkMessageType.EntitySpawn:
                    if (!IsCurrentMatchPacket(packet))
                    {
                        NetworkLog.Warning("忽略 MatchId 不匹配或验证前到达的 EntitySpawn。");
                        break;
                    }

                    EntitySpawnMessage spawn = NetworkProtocol.DeserializeEntitySpawn(packet.Payload);

                    if (despawnedEntityTicks.ContainsKey(spawn.EntityId))
                    {
                        NetworkLog.Warning($"忽略已删除 EntityId {spawn.EntityId} 的旧 EntitySpawn。");
                        break;
                    }

                    if (knownEntitySpawns.TryGetValue(spawn.EntityId, out KnownEntitySpawn knownSpawn))
                    {
                        EntitySpawnMessage knownMessage = knownSpawn.Message;

                        if (knownMessage.EntityType != spawn.EntityType || knownMessage.PrefabId != spawn.PrefabId ||
                            knownMessage.OwnerPlayerId != spawn.OwnerPlayerId)
                        {
                            NetworkLog.Error($"收到冲突的重复 EntityId {spawn.EntityId}：" +
                                $"已有 Type={knownMessage.EntityType}, PrefabId={knownMessage.PrefabId}, Owner={knownMessage.OwnerPlayerId}；" +
                                $"新消息 Type={spawn.EntityType}, PrefabId={spawn.PrefabId}, Owner={spawn.OwnerPlayerId}。");
                        }
                        else
                        {
                            NetworkLog.Warning($"收到重复 EntitySpawn：EntityId {spawn.EntityId}，未重复创建表现对象。");
                        }

                        break;
                    }

                    knownEntitySpawns.Add(spawn.EntityId, new KnownEntitySpawn(spawn, packet.Header.ServerTick));
                    EntitySpawnReceived?.Invoke(spawn, packet.Header.ServerTick);
                    break;
                case NetworkMessageType.EntityDespawn:
                    if (!IsCurrentMatchPacket(packet))
                    {
                        NetworkLog.Warning("忽略 MatchId 不匹配或验证前到达的 EntityDespawn。");
                        break;
                    }

                    EntityDespawnMessage despawn = NetworkProtocol.DeserializeEntityDespawn(packet.Payload);

                    if (despawnedEntityTicks.TryGetValue(despawn.EntityId, out uint previousDespawnTick) &&
                        previousDespawnTick >= packet.Header.ServerTick)
                    {
                        NetworkLog.Warning($"收到重复或过期 EntityDespawn：EntityId {despawn.EntityId}。");
                        break;
                    }

                    knownEntitySpawns.Remove(despawn.EntityId);
                    despawnedEntityTicks[despawn.EntityId] = packet.Header.ServerTick;
                    EntityDespawnReceived?.Invoke(despawn, packet.Header.ServerTick);
                    break;
                case NetworkMessageType.BattleEvent:
                    if (!IsCurrentMatchPacket(packet))
                    {
                        NetworkLog.Warning("忽略 MatchId 不匹配或验证前到达的 BattleEvent。");
                        break;
                    }

                    BattleEventMessage battleEvent = NetworkProtocol.DeserializeBattleEvent(packet.Payload);
                    BattleEventReceived?.Invoke(battleEvent, packet.Header.ServerTick);
                    break;
                case NetworkMessageType.ConnectionRejected:
                    ConnectionRejectedMessage rejected = NetworkProtocol.DeserializeConnectionRejected(packet.Payload);
                    StatusText = $"服务器拒绝：{rejected.Reason}";
                    NetworkLog.Error(StatusText);
                    ConnectionFailed?.Invoke(StatusText);
                    Disconnect();
                    break;
                default:
                    NetworkLog.Warning($"客户端收到当前阶段不支持的消息 {packet.Header.MessageType}。");
                    break;
            }
        }
        catch (Exception exception)
        {
            NetworkLog.Error($"客户端处理消息失败：{exception.Message}");
            Disconnect();
        }
    }

    private static bool IsCurrentMatchPacket(NetworkPacket packet)
    {
        return NetworkRuntime.MatchId != 0 && packet.Header.MatchId == NetworkRuntime.MatchId;
    }

    private void Send(NetworkMessageType messageType, byte[] payload, uint serverTick, long matchId)
    {
        lock (sendLock)
        {
            NetworkPacketCodec.WritePacket(stream, messageType, payload, ++sendSequence, serverTick, matchId);
        }
    }

    private enum ClientEventType
    {
        TcpConnected,
        Packet,
        Disconnected
    }

    private readonly struct ClientEvent
    {
        private ClientEvent(ClientEventType type, NetworkPacket packet, string error)
        {
            Type = type;
            Packet = packet;
            Error = error;
        }

        public ClientEventType Type { get; }
        public NetworkPacket Packet { get; }
        public string Error { get; }

        public static ClientEvent Connected() => new ClientEvent(ClientEventType.TcpConnected, default, null);
        public static ClientEvent Received(NetworkPacket packet) => new ClientEvent(ClientEventType.Packet, packet, null);
        public static ClientEvent Disconnected(string error) => new ClientEvent(ClientEventType.Disconnected, default, error);
    }

    private readonly struct KnownEntitySpawn
    {
        public KnownEntitySpawn(EntitySpawnMessage message, uint serverTick)
        {
            Message = message;
            ServerTick = serverTick;
        }

        public EntitySpawnMessage Message { get; }
        public uint ServerTick { get; }
    }
}
