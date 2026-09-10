using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class NetworkWindowsClientBuilder
{
    public static string BuildDefault()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("打包前必须退出 Play Mode。");

        string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
        if (scenes.Length == 0) throw new InvalidOperationException("Build Settings 中没有启用的场景。");

        string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
        string unityRoot = projectRoot != null ? Directory.GetParent(projectRoot)?.FullName : null;
        if (string.IsNullOrEmpty(unityRoot)) throw new InvalidOperationException("无法解析 Unity 工作目录。");

        string outputPath = Path.Combine(unityRoot, "打包文件", "123", "Shadow Hunter.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        BuildOptions buildOptions = EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None;
        if (EditorUserBuildSettings.allowDebugging) buildOptions |= BuildOptions.AllowDebugging;

        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputPath,
            target = BuildTarget.StandaloneWindows64,
            options = buildOptions
        });

        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Windows 客户端打包失败：{report.summary.result}，错误 {report.summary.totalErrors} 个。");

        return $"Windows 客户端打包成功：{outputPath}，大小 {report.summary.totalSize} bytes。";
    }
}
