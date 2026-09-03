using System.Collections.Concurrent;
using UnityEngine;

public static class NetworkLog
{
    private enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    private readonly struct Entry
    {
        public Entry(LogLevel level, string message)
        {
            Level = level;
            Message = message;
        }

        public LogLevel Level { get; }
        public string Message { get; }
    }

    private static readonly ConcurrentQueue<Entry> Entries = new ConcurrentQueue<Entry>();

    public static string LastError { get; private set; } = string.Empty;

    public static void Info(string message) => Entries.Enqueue(new Entry(LogLevel.Info, message));
    public static void Warning(string message) => Entries.Enqueue(new Entry(LogLevel.Warning, message));
    public static void Error(string message) => Entries.Enqueue(new Entry(LogLevel.Error, message));

    public static void FlushToUnityConsole()
    {
        while (Entries.TryDequeue(out Entry entry))
        {
            string message = $"[Network] {entry.Message}";

            switch (entry.Level)
            {
                case LogLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                case LogLevel.Error:
                    LastError = entry.Message;
                    Debug.LogError(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
            }
        }
    }
}
