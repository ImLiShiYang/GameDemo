public static class NetworkRuntime
{
    public const int DefaultServerPort = 7777;
    public const int DefaultTickRate = 20;
    public const int DefaultSnapshotRate = 10;
    public const string DefaultServerAddress = "127.0.0.1";
    public const string DefaultGameSceneAddress = "Scene/Main";

    public static NetworkRole Role { get; internal set; } = NetworkRole.Offline;
    public static int LocalPlayerId { get; internal set; }
    public static int LocalPlayerEntityId { get; internal set; }
    public static long MatchId { get; internal set; }
    public static uint ServerTick { get; internal set; }

    public static bool IsOffline => Role == NetworkRole.Offline;
    public static bool IsServer => Role == NetworkRole.Server;
    public static bool IsClient => Role == NetworkRole.Client;

    internal static void ResetSession()
    {
        LocalPlayerId = 0;
        LocalPlayerEntityId = 0;
        MatchId = 0;
        ServerTick = 0;
    }
}
