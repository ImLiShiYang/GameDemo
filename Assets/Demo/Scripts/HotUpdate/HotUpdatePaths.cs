using System;
using System.IO;
using UnityEngine;

/// <summary>
/// 统一管理热更新相关路径。
///
/// 主要负责：
/// 1. 定义 Manifest 文件名和热更新文件夹名。
/// 2. 获取本地缓存目录。
/// 3. 获取安装包内置热更新目录。
/// 4. 根据相对路径生成安全的完整文件路径。
/// </summary>
public static class HotUpdatePaths
{
    // 热更新清单文件名。
    // Manifest 中记录版本号、文件路径、文件大小和 Hash 等信息。
    public const string ManifestFileName = "manifest.json";

    // 热更新实际文件所在的文件夹名称。
    // 例如：
    // HotUpdate/files/Lua/Skill/SkillBase.lua
    public const string FilesDirectoryName = "files";

    /// <summary>
    /// 本地热更新缓存根目录。
    ///
    /// persistentDataPath 是 Unity 提供的持久化目录，
    /// 游戏关闭或者重新启动后，该目录中的文件仍然存在。
    ///
    /// 最终路径类似：
    /// persistentDataPath/HotUpdate
    /// </summary>
    public static string CacheRoot => Path.Combine(Application.persistentDataPath, "HotUpdate");

    /// <summary>
    /// 本地缓存中的热更新文件目录。
    ///
    /// 最终路径类似：
    /// persistentDataPath/HotUpdate/files
    /// </summary>
    public static string CacheFilesRoot => Path.Combine(CacheRoot, FilesDirectoryName);

    /// <summary>
    /// 本地缓存中的 Manifest 路径。
    ///
    /// 最终路径类似：
    /// persistentDataPath/HotUpdate/manifest.json
    /// </summary>
    public static string CacheManifestPath => Path.Combine(CacheRoot, ManifestFileName);

    /// <summary>
    /// 安装包内置的热更新根目录。
    ///
    /// StreamingAssets 中保存的是随游戏安装包一起发布的初始热更新内容，
    /// 当本地缓存不存在或者损坏时，可以从这里恢复。
    ///
    /// 最终路径类似：
    /// StreamingAssets/HotUpdate
    /// </summary>
    public static string BuiltInRoot => Path.Combine(Application.streamingAssetsPath, "HotUpdate");

    /// <summary>
    /// 安装包内置的 Manifest 路径。
    ///
    /// 最终路径类似：
    /// StreamingAssets/HotUpdate/manifest.json
    /// </summary>
    public static string BuiltInManifestPath => Path.Combine(BuiltInRoot, ManifestFileName);

    /// <summary>
    /// 根据热更新文件的相对路径，获取本地缓存中的完整路径。
    ///
    /// 例如传入：
    /// Lua/Skill/SkillBase.lua
    ///
    /// 返回类似：
    /// persistentDataPath/HotUpdate/files/Lua/Skill/SkillBase.lua
    /// </summary>
    public static string GetCacheFilePath(string relativePath)
    {
        return GetSafePath(CacheFilesRoot, relativePath);
    }

    /// <summary>
    /// 根据热更新文件的相对路径，获取安装包内置文件的完整路径。
    ///
    /// 例如传入：
    /// Lua/Skill/SkillBase.lua
    ///
    /// 返回类似：
    /// StreamingAssets/HotUpdate/files/Lua/Skill/SkillBase.lua
    /// </summary>
    public static string GetBuiltInFilePath(string relativePath)
    {
        return GetSafePath(Path.Combine(BuiltInRoot, FilesDirectoryName), relativePath);
    }

    /// <summary>
    /// 统一相对路径格式。
    ///
    /// Windows 常用：
    /// Lua\Skill\SkillBase.lua
    ///
    /// 转换为：
    /// Lua/Skill/SkillBase.lua
    ///
    /// 同时去掉路径开头的 /。
    /// </summary>
    public static string NormalizeRelativePath(string relativePath)
    {
        return relativePath?.Replace('\\', '/').TrimStart('/');
    }

    /// <summary>
    /// 根据根目录和相对路径生成安全的完整路径。
    ///
    /// 除了拼接路径之外，还会检查相对路径是否合法，
    /// 防止通过 ../ 等方式访问热更新目录之外的文件。
    /// </summary>
    public static string GetSafePath(string rootPath, string relativePath)
    {
        // 先把相对路径统一成标准格式。
        string normalizedRelativePath = NormalizeRelativePath(relativePath);

        // 路径不能为空，也不能直接传入绝对路径。
        //
        // 合法：
        // Lua/Skill/SkillBase.lua
        //
        // 非法：
        // C:/Windows/test.txt
        if (string.IsNullOrWhiteSpace(normalizedRelativePath) || Path.IsPathRooted(normalizedRelativePath))
        {
            throw new InvalidDataException($"热更新文件路径非法：{relativePath}");
        }

        // 获取热更新根目录的完整绝对路径。
        //
        // TrimEnd 用来去掉末尾的 / 或 \，
        // 避免后面判断目录前缀时出现问题。
        string fullRootPath = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // 将根目录和相对路径拼接起来，
        // 再转换成规范的完整绝对路径。
        //
        // 例如：
        // rootPath = C:/Game/HotUpdate/files
        // relativePath = Lua/Skill.lua
        //
        // 最终：
        // C:/Game/HotUpdate/files/Lua/Skill.lua
        string fullPath = Path.GetFullPath(Path.Combine(fullRootPath, normalizedRelativePath));

        // 给根目录末尾补上路径分隔符。
        //
        // 例如：
        // C:/Game/HotUpdate/files/
        //
        // 后面会使用 StartsWith 判断最终路径是否仍然属于这个目录。
        string requiredPrefix = fullRootPath + Path.DirectorySeparatorChar;

        // 检查最终路径是否仍然位于指定的热更新目录中。
        //
        // 例如有人传入：
        // ../../SaveData.json
        //
        // Path.GetFullPath 解析之后可能变成：
        // C:/Game/SaveData.json
        //
        // 这个路径已经不属于 HotUpdate/files，
        // 因此直接抛出异常。
        if (!fullPath.StartsWith(requiredPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"热更新文件越过缓存目录：{relativePath}");
        }

        // 路径检查通过，返回最终完整路径。
        return fullPath;
    }
}