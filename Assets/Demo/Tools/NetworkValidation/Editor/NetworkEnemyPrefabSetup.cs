using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public sealed class NetworkEnemyPrefabSetup : IPreprocessBuildWithReport
{
    private const string Path = "Assets/Demo/Resources/NetworkEnemyPrefabs.asset";
    public int callbackOrder => 0;
    static NetworkEnemyPrefabSetup() { EditorApplication.delayCall += EnsureAsset; }
    public void OnPreprocessBuild(BuildReport report) { EnsureAsset(); }

    public static void EnsureAsset()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        NetworkEnemyPrefabs asset = AssetDatabase.LoadAssetAtPath<NetworkEnemyPrefabs>(Path);
        if (asset != null && asset.Melee != null && asset.Ranged != null && asset.Boss != null && asset.Projectile != null) return;
        if (!AssetDatabase.IsValidFolder("Assets/Demo/Resources")) AssetDatabase.CreateFolder("Assets/Demo", "Resources");
        bool create = asset == null;
        if (create) asset = ScriptableObject.CreateInstance<NetworkEnemyPrefabs>();
        asset.Melee = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Enemy_Melee.prefab");
        asset.Ranged = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Enemy_Range.prefab");
        asset.Boss = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Boss.prefab");
        if (asset.Melee == null || asset.Ranged == null || asset.Boss == null) throw new BuildFailedException("缺少单机敌人预制体。");
        SerializedObject ai = new SerializedObject(asset.Ranged.GetComponent<EnemyAIController>());
        asset.Projectile = (ai.FindProperty("homingProjectilePrefab")?.objectReferenceValue as EnemyHomingProjectile)?.gameObject;
        if (create) AssetDatabase.CreateAsset(asset, Path);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
    }
}
