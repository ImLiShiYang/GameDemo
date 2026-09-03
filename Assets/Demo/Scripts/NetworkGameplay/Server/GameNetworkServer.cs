using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// 游戏服务器的 TCP 入口和固定 Tick 驱动器。
/// 后台线程只接受连接、读取字节并放入队列；Unity 主线程在 Update 中验证消息、推进世界 Tick 并触发状态快照广播。
/// </summary>
public sealed class GameNetworkServer : MonoBehaviour
{
    // 所有已接入 TCP 的连接，包含尚未通过 ConnectRequest 验证的连接；供停止服务器时逐一关闭。
    private readonly ConcurrentDictionary<int, ServerConnection> allConnections = new ConcurrentDictionary<int, ServerConnection>();
    // 后台接收线程写入、Unity 主线程读取的跨线程消息队列；队列中的项目可能是数据包或断线通知。
    private readonly ConcurrentQueue<InboundItem> inboundItems = new ConcurrentQueue<InboundItem>();
    // 已通过身份验证的玩家连接；键为 PlayerId，只有这些连接会收到世界快照。
    private readonly Dictionary<int, ServerConnection> players = new Dictionary<int, ServerConnection>();

    // 监听指定端口的新 TCP 客户端。
    private TcpListener listener;
    // 专门阻塞等待新连接的后台线程。
    private Thread acceptThread;
    // 服务器是否仍在监听和推进 Tick；volatile 让后台线程及时看到主线程的停止操作。
    private volatile bool running;
    // 为每条 TCP 连接生成唯一的内部连接编号。
    private int nextConnectionId;
    // 累计未转换成固定 Tick 的真实时间。
    private float tickAccumulator;
    // 当前监听的端口，仅用于记录和日志。
    private int port;
    private long sentMessageCount;
    private long receivedMessageCount;

    // 当前通过验证并在线的玩家数量。
    public int ConnectedPlayerCount => players.Count;
    // 本次服务器启动生成的对局编号。
    public long MatchId { get; private set; }
    // 已执行的服务器逻辑 Tick 编号。
    public uint ServerTick { get; private set; }
    public long SentMessageCount => Interlocked.Read(ref sentMessageCount);
    public long ReceivedMessageCount => Interlocked.Read(ref receivedMessageCount);

    // 玩家通过 ConnectRequest 验证后通知 ServerPlayerManager 创建权威玩家实体。
    public event Action<int, int> PlayerAuthenticated;
    // 已验证玩家断开后通知 ServerPlayerManager 删除权威玩家实体。
    public event Action<int, int> PlayerDisconnected;
    // 主线程解析到 ClientInput 后通知 ServerPlayerManager 保存最新输入。
    public event Action<int, ClientInputMessage> ClientInputReceived;
    // 每次服务器固定 Tick 时通知 ServerPlayerManager 模拟权威世界。
    public event Action<uint, float> ServerTicked;

    public void StartServer(int listenPort)
    {
        // 已运行时不重复创建监听器和接收线程。
        if (running)
        {
            return;
        }

        // 保存实际监听端口。
        port = listenPort;
        // 为本次进程生成新的对局编号，避免旧连接的数据混入新对局。
        MatchId = CreateMatchId();
        // 同步写入全局运行时状态，供调试面板和其他服务器组件读取。
        NetworkRuntime.MatchId = MatchId;
        // 在所有本机网卡上监听指定 TCP 端口。
        listener = new TcpListener(IPAddress.Any, port);
        // 开始监听，积压队列最多暂存 8 个尚未被 Accept 的连接。
        listener.Start(8);
        // 在启动接收线程前标记服务器运行中。
        running = true;
        // 创建后台 Accept 线程；它不会阻止进程退出。
        acceptThread = new Thread(AcceptLoop)
        {
            IsBackground = true, 
            Name = "GameNetworkServer.Accept" 
        };
        // 启动等待新 TCP 客户端的线程。
        acceptThread.Start();
        // 记录可用于验收的启动信息。
        NetworkLog.Info($"服务器已启动，端口 {port}，MatchId {MatchId}。");
    }

    public void StopServer()
    {
        // 未运行时没有资源需要再次释放。
        if (!running)
        {
            return;
        }

        // 先让 AcceptLoop 和每条连接的接收循环结束。
        running = false;

        try
        {
            // 停止监听会解除 AcceptTcpClient 的阻塞。
            listener?.Stop();
        }
        catch (SocketException)
        {
            // 监听器可能已被系统或前一次关闭释放，此处可安全忽略。
        }

        // 主动关闭包含未认证连接在内的全部 TCP socket。
        foreach (ServerConnection connection in allConnections.Values)
        {
            // Close 内部保证重复调用不会重复关闭 socket。
            connection.Close();
        }

        // 清空内部连接索引。
        allConnections.Clear();
        // 清空已验证玩家索引；玩家表现实体由 PlayerDisconnected/场景销毁流程处理。
        players.Clear();
        // 记录停止完成。
        NetworkLog.Info("服务器已停止。");
    }

    private void Update()
    {
        // 先在 Unity 主线程消费后台线程收到的连接、输入和断线事件。
        ProcessInboundItems();

        // 停止服务器后不再推进权威世界时间。
        if (!running)
        {
            return;
        }

        // 将每秒 Tick 数换算为单个 Tick 的固定秒数；当前 20Hz 即 0.05 秒。
        float tickInterval = 1f / NetworkRuntime.DefaultTickRate;
        // 累加真实时间，不受游戏 Time.timeScale 影响。
        tickAccumulator += Time.unscaledDeltaTime;
        // 限制单帧最多补 5 个 Tick，避免卡顿后单帧模拟无限追赶。
        int ticksThisFrame = 0;

        // 只要累计时间足够就执行固定 Tick。
        while (tickAccumulator >= tickInterval && ticksThisFrame < 5)
        {
            // 消耗一个 Tick 对应的累计时间。
            tickAccumulator -= tickInterval;
            // 递增权威时间轴。
            ServerTick++;
            // 通知 ServerPlayerManager 依据最新输入模拟所有玩家，并按需要广播快照。
            ServerTicked?.Invoke(ServerTick, tickInterval);
            // 记录本帧已补的 Tick 数。
            ticksThisFrame++;
        }

        // 让调试面板和其他组件读取到最新服务器 Tick。
        NetworkRuntime.ServerTick = ServerTick;
    }

    private void OnDestroy()
    {
        // NetworkBootstrap 销毁本组件时，确保监听端口和 TCP 连接得到释放。
        StopServer();
    }

    private void AcceptLoop()
    {
        // 后台线程持续等待新 TCP 连接，直到 StopServer 将 running 设为 false。
        while (running)
        {
            try
            {
                // 阻塞等待客户端连接；listener.Stop 会使其抛出 SocketException。
                TcpClient client = listener.AcceptTcpClient();
                // 禁用 Nagle 合包，减少小型输入包和快照包的发送延迟。
                client.NoDelay = true;
                // 为该 socket 生成与 PlayerId 无关的唯一内部编号。
                int connectionId = Interlocked.Increment(ref nextConnectionId);
                // 创建连接包装器，并把后台收包结果写入共享队列。
                ServerConnection connection = new ServerConnection(connectionId, client, inboundItems);
                // 即使尚未认证，也登记到总连接表以便停止服务器时关闭。
                allConnections[connectionId] = connection;
                // 为这一条连接启动独立的后台收包线程。
                connection.StartReceiving();
                // 连接刚接入不代表认证成功，只记录远端地址。
                NetworkLog.Info($"TCP 客户端已接入：{connection.RemoteEndPoint}。");
            }
            catch (SocketException exception)
            {
                // 正常关闭监听器也会触发 SocketException，只有仍在运行时才作为错误记录。
                if (running)
                {
                    NetworkLog.Error($"接受客户端连接失败：{exception.Message}");
                }
            }
            catch (Exception exception)
            {
                // 记录其他不可预期的接受线程异常，并继续下一轮接受。
                if (running)
                {
                    NetworkLog.Error($"接受线程异常：{exception.Message}");
                }
            }
        }
    }

    private void ProcessInboundItems()
    {
        // 直到队列清空；这里运行在 Unity 主线程，因此可安全触发涉及场景对象的事件。
        while (inboundItems.TryDequeue(out InboundItem item))
        {
            // 收包线程结束时会放入断线项目，而不是普通网络包。
            if (item.Disconnected)
            {
                // 从连接表和玩家表清理，并通知权威玩家管理器。
                HandleDisconnected(item.Connection, item.Error);
                // 此项目已经处理，不要继续当作正常数据包解析。
                continue;
            }

            // 根据当前认证阶段和消息类型处理正常数据包。
            HandlePacket(item.Connection, item.Packet);
        }
    }

    private void HandlePacket(ServerConnection connection, NetworkPacket packet)
    {
        try
        {
            if (!connection.TryAcceptIncomingSequence(packet.Header.Sequence))
            {
                NetworkLog.Warning($"服务器过滤 Connection {connection.ConnectionId} 的重复或过期消息 Sequence {packet.Header.Sequence}。");
                return;
            }

            Interlocked.Increment(ref receivedMessageCount);
            // 未认证连接只能先发送 ConnectRequest，防止未分配 PlayerId 的连接执行游戏操作。
            if (!connection.IsAuthenticated)
            {
                // 第一条消息类型不正确时立即说明原因并关闭连接。
                if (packet.Header.MessageType != NetworkMessageType.ConnectRequest)
                {
                    Reject(connection, "连接建立后的第一条消息必须是 ConnectRequest。");
                    return;
                }

                // 解析并验证申请的 PlayerId、协议版本和名称。
                HandleConnectRequest(connection, packet);
                // ConnectRequest 已处理，不继续执行已认证阶段的逻辑。
                return;
            }

            // 已认证客户端的每条游戏消息必须属于当前服务器的 MatchId。
            if (packet.Header.MatchId != MatchId)
            {
                NetworkLog.Warning($"忽略 Player {connection.PlayerId} 的错误 MatchId 消息。");
                return;
            }

            // 当前 MVP 已认证阶段只支持客户端输入消息。
            if (packet.Header.MessageType == NetworkMessageType.ClientInput)
            {
                // 反序列化并校验输入字段，例如拒绝 NaN 或非法浮点数。
                ClientInputMessage input = NetworkProtocol.DeserializeClientInput(packet.Payload);
                // 不直接改 Transform；交给 ServerPlayerManager 在固定 Tick 中处理。
                ClientInputReceived?.Invoke(connection.PlayerId, input);
                return;
            }

            // 已认证但消息类型未实现时，仅记录警告而不影响其他玩家。
            NetworkLog.Warning($"Player {connection.PlayerId} 发送了当前阶段不支持的消息 {packet.Header.MessageType}。");
        }
        catch (Exception exception)
        {
            // 任何协议解析错误都拒绝该连接，避免不可信数据继续进入权威世界。
            Reject(connection, $"消息格式错误：{exception.Message}");
        }
    }

    private void HandleConnectRequest(ServerConnection connection, NetworkPacket packet)
    {
        // 从 ConnectRequest payload 中读取协议版本、PlayerId、构建版本和玩家名称。
        ConnectRequestMessage request = NetworkProtocol.DeserializeConnectRequest(packet.Payload);

        // 客户端和服务器必须使用同一协议版本，否则字段含义可能不一致。
        if (request.ProtocolVersion != NetworkPacketHeader.CurrentProtocolVersion)
        {
            Reject(connection, $"协议版本不一致，服务器版本为 {NetworkPacketHeader.CurrentProtocolVersion}。");
            return;
        }

        // 当前双人 MVP 只分配 1 号和 2 号玩家席位。
        if (request.PlayerId < 1 || request.PlayerId > 2)
        {
            Reject(connection, "MVP 仅允许 PlayerId 1 或 2。");
            return;
        }

        // 相同 PlayerId 已在线时拒绝后来者，避免两个客户端同时控制同一实体。
        if (players.ContainsKey(request.PlayerId))
        {
            Reject(connection, $"PlayerId {request.PlayerId} 已经在线。");
            return;
        }

        // 写入认证后的玩家身份；当前 EntityId 规则为 Player 1→1001、Player 2→1002。
        connection.Authenticate(request.PlayerId, 1000 + request.PlayerId, request.PlayerName);
        // 加入已认证玩家表，从现在起可接收输入和快照。
        players.Add(connection.PlayerId, connection);
        // 构造 Welcome，告诉客户端服务器最终接受的身份、对局和同步频率。
        WelcomeMessage welcome = new WelcomeMessage
        {
            MatchId = MatchId,
            AssignedPlayerId = connection.PlayerId,
            PlayerEntityId = connection.PlayerEntityId,
            ServerTick = ServerTick,
            TickRate = NetworkRuntime.DefaultTickRate,
            SnapshotRate = NetworkRuntime.DefaultSnapshotRate
        };
        // 向刚认证的客户端发送 Welcome；它收到后才会进入 Main 场景。
        connection.Send(NetworkMessageType.Welcome, NetworkProtocol.Serialize(welcome), ServerTick, MatchId);
        Interlocked.Increment(ref sentMessageCount);
        // 通知 ServerPlayerManager 创建这名玩家对应的权威场景实体。
        PlayerAuthenticated?.Invoke(connection.PlayerId, connection.PlayerEntityId);
        // 记录认证成功。
        NetworkLog.Info($"Player {connection.PlayerId} 验证成功，EntityId {connection.PlayerEntityId}，名称 {connection.PlayerName}。");
    }

    public void BroadcastSnapshot(WorldSnapshotMessage snapshot)
    {
        // 整份世界状态只序列化一次，随后复用相同字节发送给所有在线玩家。
        byte[] payload = NetworkProtocol.Serialize(snapshot);

        // 仅向已通过 ConnectRequest 验证的玩家广播。
        foreach (ServerConnection connection in players.Values)
        {
            try
            {
                // 包头携带对应的服务器 Tick 和 MatchId，客户端据此丢弃旧快照或错误对局快照。
                connection.Send(NetworkMessageType.WorldSnapshot, payload, snapshot.ServerTick, MatchId);
                Interlocked.Increment(ref sentMessageCount);
            }
            catch (Exception exception)
            {
                // 发送失败通常代表对端已断开；先关闭，接收线程/队列随后会完成清理。
                NetworkLog.Warning($"向 Player {connection.PlayerId} 发送快照失败：{exception.Message}");
                connection.Close();
            }
        }
    }

    /// <summary>
    /// 向指定已认证玩家补发一个当前仍存在的实体，供晚加入客户端重建世界。
    /// </summary>
    public void SendEntitySpawn(int playerId, EntitySpawnMessage message)
    {
        if (!players.TryGetValue(playerId, out ServerConnection connection))
        {
            NetworkLog.Warning($"无法向未知 Player {playerId} 发送 EntitySpawn {message.EntityId}。");
            return;
        }

        try
        {
            connection.Send(NetworkMessageType.EntitySpawn, NetworkProtocol.Serialize(message), ServerTick, MatchId);
            Interlocked.Increment(ref sentMessageCount);
        }
        catch (Exception exception)
        {
            NetworkLog.Warning($"向 Player {playerId} 发送 EntitySpawn {message.EntityId} 失败：{exception.Message}");
            connection.Close();
        }
    }

    /// <summary>
    /// 向所有已认证玩家可靠广播实体创建消息。
    /// </summary>
    public void BroadcastEntitySpawn(EntitySpawnMessage message)
    {
        byte[] payload = NetworkProtocol.Serialize(message);

        foreach (ServerConnection connection in players.Values)
        {
            try
            {
                connection.Send(NetworkMessageType.EntitySpawn, payload, ServerTick, MatchId);
                Interlocked.Increment(ref sentMessageCount);
            }
            catch (Exception exception)
            {
                NetworkLog.Warning($"向 Player {connection.PlayerId} 发送 EntitySpawn {message.EntityId} 失败：{exception.Message}");
                connection.Close();
            }
        }
    }

    /// <summary>
    /// 向所有已认证玩家可靠广播实体删除消息。
    /// </summary>
    public void BroadcastEntityDespawn(EntityDespawnMessage message)
    {
        byte[] payload = NetworkProtocol.Serialize(message);

        foreach (ServerConnection connection in players.Values)
        {
            try
            {
                connection.Send(NetworkMessageType.EntityDespawn, payload, ServerTick, MatchId);
                Interlocked.Increment(ref sentMessageCount);
            }
            catch (Exception exception)
            {
                NetworkLog.Warning($"向 Player {connection.PlayerId} 发送 EntityDespawn {message.EntityId} 失败：{exception.Message}");
                connection.Close();
            }
        }
    }

    /// <summary>
    /// 广播已经由服务器确认的伤害或死亡结果。客户端没有发送该消息的入口。
    /// </summary>
    public void BroadcastBattleEvent(BattleEventMessage message)
    {
        byte[] payload = NetworkProtocol.Serialize(message);

        foreach (ServerConnection connection in players.Values)
        {
            try
            {
                connection.Send(NetworkMessageType.BattleEvent, payload, ServerTick, MatchId);
                Interlocked.Increment(ref sentMessageCount);
            }
            catch (Exception exception)
            {
                NetworkLog.Warning($"向 Player {connection.PlayerId} 发送战斗事件失败：{exception.Message}");
                connection.Close();
            }
        }
    }

    private void Reject(ServerConnection connection, string reason)
    {
        try
        {
            // 尽量先把可读拒绝原因发给客户端，再关闭 socket。
            connection.Send(
                NetworkMessageType.ConnectionRejected,
                NetworkProtocol.Serialize(new ConnectionRejectedMessage { Reason = reason }),
                ServerTick,
                MatchId
            );
            Interlocked.Increment(ref sentMessageCount);
        }
        catch (Exception exception)
        {
            // 若对端已断开则无法送达原因，只记录服务器日志。
            NetworkLog.Warning($"发送拒绝原因失败：{exception.Message}");
        }

        // 记录拒绝对象和原因。
        NetworkLog.Warning($"拒绝连接 {connection.RemoteEndPoint}：{reason}");
        // 关闭不符合协议或认证要求的连接。
        connection.Close();
    }

    private void HandleDisconnected(ServerConnection connection, string error)
    {
        // 无论是否认证，都从总连接表移除。
        allConnections.TryRemove(connection.ConnectionId, out _);

        // 只移除“当前仍对应同一条连接”的已认证玩家，避免旧连接误删同 PlayerId 的新连接。
        if (connection.IsAuthenticated && players.TryGetValue(connection.PlayerId, out ServerConnection current) && current == connection)
        {
            // 移除已认证玩家索引。
            players.Remove(connection.PlayerId);
            // 通知 ServerPlayerManager 删除玩家实体；下一份快照将不再包含它。
            PlayerDisconnected?.Invoke(connection.PlayerId, connection.PlayerEntityId);
            // 记录正常玩家断线。
            NetworkLog.Info($"Player {connection.PlayerId} 已断开，服务器继续运行。");
        }
        else
        {
            // 未认证连接没有对应场景玩家实体，只记录即可。
            NetworkLog.Info($"未认证连接 {connection.RemoteEndPoint} 已断开。");
        }

        // 有底层 IO 异常时额外记录其具体原因。
        if (!string.IsNullOrEmpty(error))
        {
            NetworkLog.Warning($"断开原因：{error}");
        }
    }

    private static long CreateMatchId()
    {
        // 生成随机 GUID 字节，作为本次对局 ID 的随机来源。
        byte[] guidBytes = Guid.NewGuid().ToByteArray();
        // 取前 8 字节转成长整数，并清除符号位，保证 MatchId 为正数。
        return BitConverter.ToInt64(guidBytes, 0) & long.MaxValue;
    }

    private readonly struct InboundItem
    {
        public InboundItem(ServerConnection connection, NetworkPacket packet)
        {
            // 保存这条数据包所属的 TCP 连接。
            Connection = connection;
            // 保存已由后台线程完整读取并解帧的网络包。
            Packet = packet;
            // 标记这是正常数据包而非断线通知。
            Disconnected = false;
            // 正常数据包没有断线错误文本。
            Error = null;
        }

        public InboundItem(ServerConnection connection, string error)
        {
            // 保存发生断线的 TCP 连接。
            Connection = connection;
            // 断线项目没有有效网络包。
            Packet = default;
            // 标记主线程应执行断线清理。
            Disconnected = true;
            // 保存底层 IO 错误；正常远端关闭时可为 null。
            Error = error;
        }

        // 这条队列项目所属的连接。
        public ServerConnection Connection { get; }
        // 正常数据包内容。
        public NetworkPacket Packet { get; }
        // 是否是断线而不是数据包。
        public bool Disconnected { get; }
        // 断线原因文本。
        public string Error { get; }
    }

    /// <summary>
    /// 单个 TCP 客户端连接的线程安全包装器。
    /// 它负责后台收包和串行发包，但不调用任何 Unity 场景 API。
    /// </summary>
    private sealed class ServerConnection
    {
        // 原始 TCP socket。
        private readonly TcpClient client;
        // socket 对应的字节读写流。
        private readonly NetworkStream stream;
        // 将后台读到的包交给 GameNetworkServer.Update 的共享队列。
        private readonly ConcurrentQueue<InboundItem> inboundItems;
        // 防止服务器快照、Welcome、拒绝消息从不同调用点同时写入同一条 stream 而交错。
        private readonly object sendLock = new object();
        // 0 表示未关闭，1 表示已关闭；Interlocked 用于跨线程原子判断。
        private int closed;
        // 此连接发出的网络包序号。
        private uint sendSequence;
        private uint lastReceivedSequence;

        public ServerConnection(int connectionId, TcpClient client, ConcurrentQueue<InboundItem> inboundItems)
        {
            // 保存内部连接编号。
            ConnectionId = connectionId;
            // 保存 TCP 客户端对象。
            this.client = client;
            this.client.ReceiveTimeout = 30000;
            this.client.SendTimeout = 5000;
            // 保存主线程消费的队列。
            this.inboundItems = inboundItems;
            // 获取 TCP 字节流，供收包和发包使用。
            stream = client.GetStream();
            // 保存远端 IP:端口，主要用于日志诊断。
            RemoteEndPoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        }

        // 服务器内部用于追踪连接的唯一编号。
        public int ConnectionId { get; }
        // 客户端远端地址文本。
        public string RemoteEndPoint { get; }
        // 是否已通过 ConnectRequest 验证。
        public bool IsAuthenticated { get; private set; }
        // 验证后分配或确认的玩家席位。
        public int PlayerId { get; private set; }
        // 验证后分配的网络实体编号。
        public int PlayerEntityId { get; private set; }
        // 客户端提交的显示名称。
        public string PlayerName { get; private set; }

        public void StartReceiving()
        {
            // 每条 TCP 连接各自拥有一个阻塞读取线程，避免一个慢客户端阻塞其他客户端接入。
            Thread thread = new Thread(ReceiveLoop)
            {
                IsBackground = true, 
                Name = $"GameNetworkServer.Receive.{ConnectionId}" 
            };
            // 启动该连接的收包循环。
            thread.Start();
        }

        public void Authenticate(int playerId, int playerEntityId, string playerName)
        {
            // 写入已验证的玩家席位。
            PlayerId = playerId;
            // 写入服务器分配的实体编号。
            PlayerEntityId = playerEntityId;
            // 空名称回退为可读默认名称。
            PlayerName = string.IsNullOrWhiteSpace(playerName) ? $"Player {playerId}" : playerName;
            // 最后才开放游戏消息处理，确保前面所有身份字段已完整写入。
            IsAuthenticated = true;
        }

        public bool TryAcceptIncomingSequence(uint sequence)
        {
            if (sequence == 0 || sequence <= lastReceivedSequence)
            {
                return false;
            }

            lastReceivedSequence = sequence;
            return true;
        }

        public void Send(NetworkMessageType messageType, byte[] payload, uint serverTick, long matchId)
        {
            // 同一 TCP 流不能并发交错写入；锁保证一个完整包连续写出。
            lock (sendLock)
            {
                // 编码包头和 payload，并递增该连接自己的发送序号。
                NetworkPacketCodec.WritePacket(stream, messageType, payload, ++sendSequence, serverTick, matchId);
            }
        }

        public void Close()
        {
            // 只有第一个调用者能把 closed 从 0 改为 1；后续调用直接返回。
            if (Interlocked.Exchange(ref closed, 1) != 0)
            {
                return;
            }

            try
            {
                // 关闭 socket 会让阻塞中的 ReadPacket/Accept 退出。
                client.Close();
            }
            catch (SocketException)
            {
                // socket 已关闭或系统已回收时可安全忽略。
            }
        }

        private void ReceiveLoop()
        {
            // 保存接收循环的异常文本；正常远端关闭保持 null。
            string error = null;

            try
            {
                // 持续读取“完整网络包”，直到主动关闭或远端关闭连接。
                while (Volatile.Read(ref closed) == 0)
                {
                    // NetworkPacketCodec 负责解决 TCP 拆包；读取完成后只入队，不触碰 Unity 场景对象。
                    inboundItems.Enqueue(new InboundItem(this, NetworkPacketCodec.ReadPacket(stream)));
                }
            }
            catch (EndOfStreamException)
            {
                // 远端正常关闭连接会导致流结束；finally 仍会发送断线通知。
            }
            catch (IOException exception)
            {
                // 主动 Close 造成的 IO 异常不是业务错误；仍在运行时才记录原因。
                if (Volatile.Read(ref closed) == 0)
                {
                    // 保存错误文本交给主线程日志输出。
                    error = exception.Message;
                }
            }
            catch (Exception exception)
            {
                // 捕获其他协议或系统异常，避免后台线程无提示退出。
                if (Volatile.Read(ref closed) == 0)
                {
                    // 保存错误文本交给主线程清理时记录。
                    error = exception.Message;
                }
            }
            finally
            {
                // 确保底层 socket 最终关闭。
                Close();
                // 通知 Unity 主线程从玩家表、连接表和权威世界中清理该连接。
                inboundItems.Enqueue(new InboundItem(this, error));
            }
        }
    }
}
