using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Sockets;
using System.Threading;

public sealed class LockstepNetworkClient : IDisposable
{
    private readonly ConcurrentQueue<LockstepFrame> receivedFrames = new ConcurrentQueue<LockstepFrame>();
    private readonly ConcurrentQueue<string> logMessages = new ConcurrentQueue<string>();
    private readonly object sendLock = new object();

    private Thread receiveThread;
    private TcpClient tcpClient;
    private NetworkStream stream;
    private volatile bool isRunning;
    private volatile bool isConnected;

    public bool IsConnected => isConnected;

    public void Start(string host, int port)
    {
        if (isRunning) throw new InvalidOperationException("The network client is already running.");

        isRunning = true;
        receiveThread = new Thread(() => ReceiveLoop(host, port))
        {
            IsBackground = true,
            Name = "Lockstep Client Receive"
        };
        receiveThread.Start();
    }

    public bool SendInput(InputMessage message)
    {
        if (!isConnected || stream == null) return false;

        try
        {
            lock (sendLock)
            {
                if (!isConnected || stream == null) return false;
                LockstepWireProtocol.WriteInput(stream, message);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException || exception is SocketException || exception is ObjectDisposedException)
        {
            logMessages.Enqueue($"Failed to send input: {exception.Message}");
            CloseConnection();
            return false;
        }
    }

    public bool TryDequeueFrame(out LockstepFrame frame)
    {
        return receivedFrames.TryDequeue(out frame);
    }

    public bool TryDequeueLog(out string message)
    {
        return logMessages.TryDequeue(out message);
    }

    public void Dispose()
    {
        isRunning = false;
        CloseConnection();
        if (receiveThread != null && receiveThread != Thread.CurrentThread) receiveThread.Join(500);
        receiveThread = null;
    }

    private void ReceiveLoop(string host, int port)
    {
        try
        {
            tcpClient = new TcpClient { NoDelay = true };
            tcpClient.Connect(host, port);
            stream = tcpClient.GetStream();
            isConnected = true;
            logMessages.Enqueue($"Connected to {host}:{port}.");

            while (isRunning && LockstepWireProtocol.TryReadFrame(stream, out LockstepFrame frame)) receivedFrames.Enqueue(frame);
            if (isRunning) logMessages.Enqueue("The server closed the connection.");
        }
        catch (Exception exception) when (exception is IOException || exception is SocketException || exception is ObjectDisposedException || exception is InvalidDataException)
        {
            if (isRunning) logMessages.Enqueue($"Network connection stopped: {exception.Message}");
        }
        finally
        {
            CloseConnection();
        }
    }

    private void CloseConnection()
    {
        isConnected = false;
        try { stream?.Close(); } catch { }
        try { tcpClient?.Close(); } catch { }
        stream = null;
        tcpClient = null;
    }
}
