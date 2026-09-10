using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class EditorNetworkClientLauncher
{
    private const string LoginScenePath = "Assets/Demo/Scenes/LoginScene.scene";
    private const string RestoreStartSceneKey = "GameDemo.EditorNetworkClient.RestoreStartScene";
    private const string PreviousStartSceneKey = "GameDemo.EditorNetworkClient.PreviousStartScene";
    public const string EnabledKey = "GameDemo.EditorNetworkClient.Enabled";
    public const string PlayerIdKey = "GameDemo.EditorNetworkClient.PlayerId";
    public const string ServerPortKey = "GameDemo.EditorNetworkClient.ServerPort";
    public const string ServerAddressKey = "GameDemo.EditorNetworkClient.ServerAddress";

    static EditorNetworkClientLauncher()
    {
        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    [MenuItem("Tools/Network Validation/Play as Network Client 1")]
    public static void PlayClient1Menu()
    {
        DebugLog(Start(1, "127.0.0.1", NetworkRuntime.DefaultServerPort));
    }

    public static string Start(int playerId, string serverAddress, int serverPort)
    {
        if (playerId < 1 || playerId > 2) throw new ArgumentOutOfRangeException(nameof(playerId));
        if (serverPort < 1 || serverPort > 65535) throw new ArgumentOutOfRangeException(nameof(serverPort));
        if (string.IsNullOrWhiteSpace(serverAddress)) throw new ArgumentException("服务器地址不能为空。", nameof(serverAddress));
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Unity 编辑器已经处于 Play Mode 或正在切换状态。");

        EditorPrefs.SetInt(PlayerIdKey, playerId);
        EditorPrefs.SetInt(ServerPortKey, serverPort);
        EditorPrefs.SetString(ServerAddressKey, serverAddress);
        EditorPrefs.SetBool(EnabledKey, true);
        SceneAsset loginScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(LoginScenePath);
        if (loginScene == null) throw new InvalidOperationException($"找不到登录场景：{LoginScenePath}");
        SceneAsset previousStartScene = EditorSceneManager.playModeStartScene;
        EditorPrefs.SetString(PreviousStartSceneKey,
            previousStartScene != null ? AssetDatabase.GetAssetPath(previousStartScene) : string.Empty);
        EditorPrefs.SetBool(RestoreStartSceneKey, true);
        EditorSceneManager.playModeStartScene = loginScene;
        EditorApplication.isPlaying = true;
        return $"Unity Game 窗口将作为 Player {playerId} 连接 {serverAddress}:{serverPort}。";
    }

    public static string Stop()
    {
        EditorPrefs.DeleteKey(EnabledKey);
        RestorePlayModeStartScene();
        if (!EditorApplication.isPlayingOrWillChangePlaymode) return "Unity 编辑器当前不在 Play Mode。";
        EditorApplication.isPlaying = false;
        return "已请求 Unity Game 窗口退出 Play Mode。";
    }

    private static void DebugLog(string message) => UnityEngine.Debug.Log(message);

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode) RestorePlayModeStartScene();
    }

    private static void RestorePlayModeStartScene()
    {
        if (!EditorPrefs.GetBool(RestoreStartSceneKey, false)) return;
        string previousPath = EditorPrefs.GetString(PreviousStartSceneKey, string.Empty);
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previousPath)
            ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previousPath);
        EditorPrefs.DeleteKey(RestoreStartSceneKey);
        EditorPrefs.DeleteKey(PreviousStartSceneKey);
    }
}
