using System;

/// <summary>
/// 一份完整的热更新清单，对应 manifest.json 的根对象。
/// JsonUtility 会根据字段名称，将 JSON 数据写入这些 public 字段。
/// </summary>
[Serializable]
public sealed class HotUpdateManifest
{
    // 热更新内容版本，例如 "1.0.2"。
    // 这是 Lua 和配置内容的版本，不是客户端安装包版本。
    public string version;

    // 使用这份热更新内容所要求的最低客户端版本。
    // 用于防止新版 Lua 调用旧客户端中不存在的 C# 接口。
    public string minimumAppVersion;

    // 这份热更新包包含的全部文件记录。
    // JSON 中的 files 数组会被反序列化到这里。
    public HotUpdateFileEntry[] files;
}

/// <summary>
/// Manifest 中的一条文件记录，用于描述一个热更新文件应该具备的路径、大小和 Hash。
/// </summary>
[Serializable]
public sealed class HotUpdateFileEntry
{
    // 文件相对于热更新 files 目录的路径，例如 "Lua/Skill/SkillConfig.lua"。
    public string path;

    // 文件的预期字节数。
    // 使用 long 而不是 int，可以支持超过 2 GB 的文件，也与 FileInfo.Length 的类型保持一致。
    public long size;

    // 文件内容对应的 SHA-256 字符串，共 64 个十六进制字符。
    // 客户端通过它判断本地文件是否变化、下载结果是否完整。
    public string sha256;
}

/// <summary>
/// 一次热更新流程的最终结果。
/// readonly struct 表示这是一个轻量值类型，并且创建后不能再修改内部状态。
/// </summary>
public readonly struct HotUpdateResult
{
    // 本次流程是否成功。
    // 使用本地缓存继续运行也属于成功，因此 Cached 结果的 Success 同样为 true。
    public bool Success { get; }

    // 是否因为未配置远端地址、远端不可用或版本不兼容而使用了本地缓存版本。
    public bool UsedCachedVersion { get; }

    // 本次流程最终确定使用的热更新内容版本。
    // 更新失败时没有可确认的版本，因此 Failed 会将它设为空字符串。
    public string Version { get; }

    // 提供给调用方的结果说明，例如“热更新完成”或具体的失败原因。
    public string Message { get; }

    // 私有构造函数，禁止外部随意组合状态。
    // 外部只能通过 Completed、Cached 和 Failed 创建语义明确的结果。
    private HotUpdateResult(bool success, bool usedCachedVersion, string version, string message)
    {
        Success = success;
        UsedCachedVersion = usedCachedVersion;
        Version = version;
        Message = message;
    }

    /// <summary>
    /// 创建“远端检查或更新正常完成”的结果。
    /// </summary>
    public static HotUpdateResult Completed(string version, string message)
    {
        // 成功，并且没有因为异常情况降级使用缓存。
        return new HotUpdateResult(true, false, version, message);
    }

    /// <summary>
    /// 创建“流程可以继续，但最终使用本地缓存内容”的结果。
    /// </summary>
    public static HotUpdateResult Cached(string version, string message)
    {
        // 本地内容可正常运行，所以 Success 为 true；同时记录本次使用了缓存版本。
        return new HotUpdateResult(true, true, version, message);
    }

    /// <summary>
    /// 创建“没有准备好可用热更新内容”的失败结果。
    /// </summary>
    public static HotUpdateResult Failed(string message)
    {
        // 失败时没有可以确认的新活动版本，因此 Version 使用空字符串。
        return new HotUpdateResult(false, false, string.Empty, message);
    }
}