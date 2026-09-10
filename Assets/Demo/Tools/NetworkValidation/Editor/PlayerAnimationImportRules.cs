using System;
using UnityEditor;
using UnityEngine;

/// <summary>玩法角色动画的统一导入规则。FBX 保留为素材源，运行时 Clip 一律不驱动角色根节点。</summary>
[InitializeOnLoad]
public sealed class PlayerAnimationImportRules : AssetPostprocessor
{
    private const string RollPath = "Assets/Animations/Ch44_nonPBR@Running Dive Roll.fbx";

    static PlayerAnimationImportRules()
    {
        EditorApplication.delayCall += EnsureRollExtraction;
    }

    private static bool IsPlayerAnimation(string path)
    {
        return path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase) &&
            (path.StartsWith("Assets/Animations/Ch44_nonPBR@", StringComparison.Ordinal) ||
             path.StartsWith("Assets/Models/player/Ch44_nonPBR@", StringComparison.Ordinal));
    }

    private void OnPreprocessModel()
    {
        if (!IsPlayerAnimation(assetPath)) return;
        ModelImporter importer = (ModelImporter)assetImporter;
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        bool extractHorizontalMotion = assetPath.Equals(RollPath, StringComparison.OrdinalIgnoreCase);
        bool changed = false;
        for (int i = 0; i < clips.Length; i++)
        {
            ModelImporterClipAnimation clip = clips[i];
            bool lockHorizontalMotion = !extractHorizontalMotion;
            if (!clip.lockRootRotation) { clip.lockRootRotation = true; changed = true; }
            if (!clip.lockRootHeightY) { clip.lockRootHeightY = true; changed = true; }
            if (clip.lockRootPositionXZ != lockHorizontalMotion) { clip.lockRootPositionXZ = lockHorizontalMotion; changed = true; }
        }
        if (changed) importer.clipAnimations = clips;
    }

    private static void EnsureRollExtraction()
    {
        ModelImporter importer = AssetImporter.GetAtPath(RollPath) as ModelImporter;
        if (importer == null) return;
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        if (clips != null && clips.Length > 0 && Array.TrueForAll(clips,
            clip => clip.lockRootRotation && clip.lockRootHeightY && !clip.lockRootPositionXZ)) return;
        AssetDatabase.ImportAsset(RollPath, ImportAssetOptions.ForceUpdate);
    }

    [MenuItem("Tools/Network Validation/Reimport Player In-Place Animations")]
    public static void ReimportAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Animations", "Assets/Models/player" });
        int count = 0;
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!IsPlayerAnimation(path)) continue;
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            count++;
        }
        Debug.Log($"玩家动画已按 In-Place 规则重新导入：{count} 个 FBX。");
    }
}
