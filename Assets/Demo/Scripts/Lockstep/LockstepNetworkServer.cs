using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

public sealed class LockstepNetworkServer : IDisposable
{
    private sealed class PendingFrame
    {
        public bool hasPlayer1Input;
        public bool hasPlayer2Input;
        public LockstepInput player1Input;
        public LockstepInput player2Input;
    }

    private sealed class ClientConnection
    {
        public int id;
        public int playerId;
        public TcpClient client;
        public NetworkStream stream;
        public Thread receiveThread;
        public readonly object sendLock = new object();
    }

    private readonly object sync = new object();
    private readonly Dictionary<int, PendingFrame> pendingFrames = new Dictionary<int, PendingFrame>();
    private readonly List<ClientConnection> clients = new List<ClientConnection>();
    private readonly ConcurrentQueue<string> logMessages = new ConcurrentQueue<string>();
    private TcpListener listener;
    private Thread acceptThread;
    private volatile bool isRunning;
    private int nextClientId;
    private int nextBroadcastTick;

    public void Start(int port)
    {
        if (isRunning) throw new InvalidOperationException("The lockstep server is already running.");
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        isRunning = true;
        acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "Lockstep Server Accept" };
        acceptThread.Start();
        logMessages.Enqueue($"Server listening on port {port}.");
    }

    public bool TryDequeueLog(out string message)
    {
        return logMessages.TryDequeue(out message);
    }

    public void Dispose()
    {
        isRunning = false;
        try { listener?.Stop(); } catch { }

        ClientConnection[] snapshot;
        lock (sync)
        {
            snapshot = clients.ToArray();
            clients.Clear();
            pendingFrames.Clear();
        }

        foreach (ClientConnection connection in snapshot) CloseSocket(connection);
        if (acceptThread != null && acceptThread != Thread.CurrentThread) acceptThread.Join(500);
        acceptThread = null;
        listener = null;
    }

    private void AcceptLoop()
    {
        while (isRunning)
        {
            try
            {
                TcpClient client = listener.AcceptTcpClient();
                client.NoDelay = true;
                ClientConnection connection = new ClientConnection
                {
                    id = Interlocked.Increment(ref nextClientId),
                    client = client,
                    stream = client.GetStream()
                };
                connection.receiveThread = new Thread(() => ReceiveLoop(connection))
                {
                    IsBackground = true,
                    Name = $"Lockstep Server Client {connection.id}"
                };
                lock (sync) clients.Add(connection);
                connection.receiveThread.Start();
                logMessages.Enqueue($"Client {connection.id} connected from {client.Client.RemoteEndPoint}.");
            }
            catch (Exception exception) when (exception is SocketException || exception is ObjectDisposedException)
            {
                if (isRunning) logMessages.Enqueue($"Failed to accept client: {exception.Message}");
            }
        }
    }

    private void ReceiveLoop(ClientConnection connection)
    {
        try
        {
            while (isRunning && LockstepWireProtocol.TryReadInput(connection.stream, out InputMessage message)) SubmitInput(connection, message);
        }
        catch (Exception exception) when (exception is IOException || exception is SocketException || exception is ObjectDisposedException || exception is InvalidDataException)
        {
            if (isRunning) logMessages.Enqueue($"Client {connection.id} network error: {exception.Message}");
        }
        finally
        {
            RemoveClient(connection);
        }
    }

    private void SubmitInput(ClientConnection connection, InputMessage message)
    {
        if (message.tick < 0 || message.playerId < 1 || message.playerId > 2) return;
        if (message.input.horizontal < -LockstepInput.AxisScale || message.input.horizontal > LockstepInput.AxisScale) return;
        if (message.input.vertical < -LockstepInput.AxisScale || message.input.vertical > LockstepInput.AxisScale) return;

        List<LockstepFrame> readyFrames = new List<LockstepFrame>();
        ClientConnection replacedConnection = null;
        bool startedNewMatch = false;
        lock (sync)
        {
            if (connection.playerId == 0)
            {
                replacedConnection = clients.Find(client => client != connection && client.playerId == message.playerId);
                // 只有从 Tick 0 开始的新客户端可以接管仍在线的同 ID 客户端。
                // 旧客户端断线重连时携带的是较大 Tick，不能反过来抢走新一局的连接。
                if (replacedConnection != null && message.tick != 0) return;
                if (replacedConnection != null) clients.Remove(replacedConnection);
                connection.playerId = message.playerId;
            }
            else if (connection.playerId != message.playerId)
            {
                return;
            }

            // 开发阶段允许新启动的客户端用 Tick 0 接管一局，避免服务器进程必须手动重启。
            if (message.tick == 0 && nextBroadcastTick > 0)
            {
                pendingFrames.Clear();
                nextBroadcastTick = 0;
                startedNewMatch = true;
            }

            if (message.tick < nextBroadcastTick) return;
            if (message.tick > nextBroadcastTick + 4) return;
            if (!pendingFrames.TryGetValue(message.tick, out PendingFrame pendingFrame))
            {
                pendingFrame = new PendingFrame();
                pendingFrames.Add(message.tick, pendingFrame);
            }

            if (message.playerId == 1 && !pendingFrame.hasPlayer1Input)
            {
                pendingFrame.player1Input = message.input;
                pendingFrame.hasPlayer1Input = true;
            }
            else if (message.playerId == 2 && !pendingFrame.hasPlayer2Input)
            {
                pendingFrame.player2Input = message.input;
                pendingFrame.hasPlayer2Input = true;
            }

            while (pendingFrames.TryGetValue(nextBroadcastTick, out PendingFrame frame) && frame.hasPlayer1Input && frame.hasPlayer2Input)
            {
                readyFrames.Add(new LockstepFrame(nextBroadcastTick, frame.player1Input, frame.player2Input));
                pendingFrames.Remove(nextBroadcastTick);
                nextBroadcastTick++;
            }
        }

        if (replacedConnection != null)
        {
            CloseSocket(replacedConnection);
            logMessages.Enqueue($"Player {message.playerId} connection was replaced by client {connection.id}.");
        }
        if (startedNewMatch) logMessages.Enqueue($"Client {connection.id} started a new match at Tick 0.");
        foreach (LockstepFrame frame in readyFrames) BroadcastFrame(frame);
    }

    private void BroadcastFrame(LockstepFrame frame)
    {
        ClientConnection[] snapshot;
        lock (sync) snapshot = clients.ToArray();

        foreach (ClientConnection connection in snapshot)
        {
            try
            {
                lock (connection.sendLock) LockstepWireProtocol.WriteFrame(connection.stream, frame);
            }
            catch (Exception exception) when (exception is IOException || exception is SocketException || exception is ObjectDisposedException)
            {
                logMessages.Enqueue($"Failed to send Tick {frame.tick} to client {connection.id}: {exception.Message}");
                RemoveClient(connection);
            }
        }
    }

    private void RemoveClient(ClientConnection connection)
    {
        bool removed;
        bool resetMatch = false;
        lock (sync)
        {
            removed = clients.Remove(connection);
            if (removed && clients.Count == 0)
            {
                pendingFrames.Clear();
                nextBroadcastTick = 0;
                resetMatch = true;
            }
        }
        CloseSocket(connection);
        if (removed) logMessages.Enqueue($"Client {connection.id} disconnected.");
        if (resetMatch) logMessages.Enqueue("All clients disconnected. Match Tick reset to 0.");
    }

    private static void CloseSocket(ClientConnection connection)
    {
        try { connection.stream?.Close(); } catch { }
        try { connection.client?.Close(); } catch { }
    }
}
