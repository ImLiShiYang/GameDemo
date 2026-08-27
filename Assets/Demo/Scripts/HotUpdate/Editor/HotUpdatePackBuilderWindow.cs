#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class HotUpdatePackBuilderWindow : EditorWindow
{
    private const string LuaSourceRelativePath = "Assets/Demo/Scripts/Lua/Core";
    private const string BuiltInOutputRelativePath = "Assets/StreamingAssets/HotUpdate";
    private const string ServerOutputRelativePath = "ServerData/HotUpdate";

    [SerializeField] private string contentVersion = "1.0.0";
    [SerializeField] private string minimumAppVersion = string.Empty;
    [SerializeField] private bool updateBuiltInBaseline = true;

    [MenuItem("Tools/Game Demo/Hot Update/Build Package")]
    private static void Open()
    {
        HotUpdatePackBuilderWindow window = GetWindow<HotUpdatePackBuilderWindow>(true, "Build Hot Update Package");
        window.minSize = new Vector2(460f, 220f);
        window.Show();
    }

    private void OnEnable()
    {
        if (string.IsNullOrWhiteSpace(minimumAppVersion))
        {
            minimumAppVersion = PlayerSettings.bundleVersion;
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Day 16 Lua 与配置热更新包", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        contentVersion = EditorGUILayout.TextField("Content Version", contentVersion);
        minimumAppVersion = EditorGUILayout.TextField("Minimum App Version", minimumAppVersion);
        updateBuiltInBaseline = EditorGUILayout.ToggleLeft("同时更新安装包内置基线（首次打包时勾选）", updateBuiltInBaseline);

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "远端包输出到 ServerData/HotUpdate/<Platform>。内置基线输出到 Assets/StreamingAssets/HotUpdate。普通热更新只生成远端包，不要覆盖已经发布的安装包基线。",
            MessageType.Info
        );

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
        {
            if (GUILayout.Button("Build Hot Update Package", GUILayout.Height(36f)))
            {
                BuildPackage();
            }
        }
    }

    private void BuildPackage()
    {
        if (!Version.TryParse(contentVersion, out _))
        {
            EditorUtility.DisplayDialog("Hot Update", "Content Version 必须是 1.0.0 形式的版本号。", "确定");
            return;
        }

        if (!string.IsNullOrWhiteSpace(minimumAppVersion) && !Version.TryParse(minimumAppVersion, out _))
        {
            EditorUtility.DisplayDialog("Hot Update", "Minimum App Version 必须是 1.0.0 形式的版本号。", "确定");
            return;
        }

        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string sourceRoot = Path.GetFullPath(Path.Combine(projectRoot, LuaSourceRelativePath));

        if (!Directory.Exists(sourceRoot))
        {
            EditorUtility.DisplayDialog("Hot Update", $"没有找到 Lua 源目录：\n{sourceRoot}", "确定");
            return;
        }

        string platformName = GetPlatformName(EditorUserBuildSettings.activeBuildTarget);
        string serverRoot = Path.Combine(projectRoot, ServerOutputRelativePath, platformName);
        HotUpdateManifest manifest = BuildOutput(sourceRoot, serverRoot);

        if (updateBuiltInBaseline)
        {
            string builtInRoot = Path.Combine(projectRoot, BuiltInOutputRelativePath);
            BuildOutput(sourceRoot, builtInRoot, manifest);
        }

        AssetDatabase.Refresh();
        Debug.Log(
            $"热更新包生成完成。\n" +
            $"内容版本：{manifest.version}\n" +
            $"文件数量：{manifest.files.Length}\n" +
            $"远端目录：{serverRoot}"
        );

        EditorUtility.RevealInFinder(serverRoot);
    }

    private HotUpdateManifest BuildOutput(string sourceRoot, string outputRoot, HotUpdateManifest existingManifest = null)
    {
        string filesRoot = Path.Combine(outputRoot, HotUpdatePaths.FilesDirectoryName);
        RecreateDirectory(filesRoot);

        string[] sourceFiles = Directory.GetFiles(sourceRoot, "*.lua", SearchOption.AllDirectories);
        Array.Sort(sourceFiles, StringComparer.OrdinalIgnoreCase);
        List<HotUpdateFileEntry> entries = new List<HotUpdateFileEntry>(sourceFiles.Length);

        foreach (string sourcePath in sourceFiles)
        {
            string luaRelativePath = Path.GetRelativePath(sourceRoot, sourcePath).Replace('\\', '/');
            string manifestPath = "Lua/" + luaRelativePath;
            string outputPath = HotUpdatePaths.GetSafePath(filesRoot, manifestPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.Copy(sourcePath, outputPath, true);

            FileInfo fileInfo = new FileInfo(sourcePath);
            entries.Add(new HotUpdateFileEntry
            {
                path = manifestPath,
                size = fileInfo.Length,
                sha256 = FileHashUtility.ComputeSha256(sourcePath)
            });
        }

        HotUpdateManifest manifest = existingManifest ?? new HotUpdateManifest
        {
            version = contentVersion.Trim(),
            minimumAppVersion = minimumAppVersion.Trim(),
            files = entries.ToArray()
        };

        if (existingManifest != null)
        {
            manifest.files = entries.ToArray();
        }

        Directory.CreateDirectory(outputRoot);
        File.WriteAllText(Path.Combine(outputRoot, HotUpdatePaths.ManifestFileName), JsonUtility.ToJson(manifest, true));
        return manifest;
    }

    private static void RecreateDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, true);
        }

        Directory.CreateDirectory(directoryPath);
    }

    private static string GetPlatformName(BuildTarget buildTarget)
    {
        switch (buildTarget)
        {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                return "Windows";
            case BuildTarget.Android:
                return "Android";
            case BuildTarget.iOS:
                return "iOS";
            case BuildTarget.WebGL:
                return "WebGL";
            default:
                return buildTarget.ToString();
        }
    }
}
#endif
