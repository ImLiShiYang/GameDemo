using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class PlayerMotionProfileSetup
{
    private const string AssetPath = "Assets/Demo/Resources/PlayerMotionProfile.asset";

    static PlayerMotionProfileSetup()
    {
        EditorApplication.delayCall += EnsureAsset;
    }

    [MenuItem("Tools/Network Validation/Ensure Player Motion Profile")]
    public static void EnsureAsset()
    {
        if (AssetDatabase.LoadAssetAtPath<PlayerMotionProfile>(AssetPath) != null) return;
        PlayerMotionProfile profile = ScriptableObject.CreateInstance<PlayerMotionProfile>();
        AssetDatabase.CreateAsset(profile, AssetPath);
        AssetDatabase.SaveAssets();
        Debug.Log($"已创建玩家运动统一配置：{AssetPath}");
    }
}
