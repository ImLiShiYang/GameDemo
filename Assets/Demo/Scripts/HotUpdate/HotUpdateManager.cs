using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public sealed class HotUpdateManager : MonoBehaviour
{
    private const int DefaultTimeoutSeconds = 10;
    private const int MaximumDownloadAttempts = 3;

    public event Action<string> StatusChanged;
    public event Action<float, long, long> ProgressChanged;

    public bool IsRunning { get; private set; }
    public bool IsReady { get; private set; }
    public string ActiveVersion { get; private set; } = string.Empty;

    /// <summary>
    /// 执行一次完整的热更新流程。
    /// </summary>
    /// <param name="remoteBaseUrl">远端热更新目录，例如：http://127.0.0.1:8000/HotUpdate/Windows</param>
    /// <param name="timeoutSeconds">单次网络请求的超时时间。</param>
    /// <returns>热更新结果，包括是否成功、是否使用缓存、当前版本和提示信息。</returns>
    public async Task<HotUpdateResult> RunAsync(string remoteBaseUrl, int timeoutSeconds = DefaultTimeoutSeconds)
    {
        // 防止同一个管理器同时运行多个热更新任务，避免多个任务同时读写缓存文件。
        if (IsRunning)
        {
            return HotUpdateResult.Failed("热更新检查已经在进行中。");
        }

        // 标记热更新开始，并暂时将内容设为未准备完成。
        IsRunning = true;
        IsReady = false;

        try
        {
            // 确保热更新缓存目录存在。目录已存在时不会清空原有内容。
            Directory.CreateDirectory(HotUpdatePaths.CacheFilesRoot);

            // 优先读取并校验本地缓存；缓存无效时，从安装包的 StreamingAssets 恢复内置内容。
            HotUpdateManifest localManifest = await PrepareLocalCacheAsync(timeoutSeconds);

            // 没有任何完整可用的本地内容时，无法继续启动游戏。
            if (localManifest == null)
            {
                return HotUpdateResult.Failed("没有找到完整的内置或本地热更新内容。");
            }

            // 先将当前活动版本设置为本地版本。远端不可用时仍可继续使用它。
            ActiveVersion = localManifest.version;

            // 未配置远端地址时跳过联网检查，直接使用已经校验通过的本地内容。
            if (string.IsNullOrWhiteSpace(remoteBaseUrl))
            {
                IsReady = true;
                ReportStatus($"使用本地内容版本 {ActiveVersion}。");
                return HotUpdateResult.Cached(ActiveVersion, "未配置远端地址，使用本地内容。");
            }

            HotUpdateManifest remoteManifest;

            try
            {
                ReportStatus("正在获取远端更新清单...");

                // 时间戳查询参数用于尽量避免获取到被浏览器、代理或服务器缓存的旧 Manifest。
                string manifestUrl = CombineUrl(remoteBaseUrl, HotUpdatePaths.ManifestFileName) + $"?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

                // 下载远端 Manifest 的原始字节数据。
                byte[] manifestBytes = await DownloadBytesAsync(manifestUrl, timeoutSeconds);

                // 将 JSON 解析成 Manifest，并检查版本、文件记录、路径和 Hash 格式是否合法。
                remoteManifest = ParseAndValidateManifest(manifestBytes, "远端");
            }
            catch (Exception exception)
            {
                // 本地内容已经校验通过，所以远端不可用时可以降级使用本地缓存。
                IsReady = true;
                ReportStatus($"远端不可用，继续使用缓存版本 {ActiveVersion}。");
                Debug.LogWarning($"检查远端热更新失败，已回退到缓存版本 {ActiveVersion}。\n{exception}", this);
                return HotUpdateResult.Cached(ActiveVersion, "远端不可用，已使用本地缓存。");
            }

            // 检查当前安装包版本是否满足远端内容要求的最低客户端版本。
            // 如果不满足，说明新 Lua 可能依赖当前客户端不存在的 C# 接口，因此继续使用本地内容。
            if (!IsApplicationVersionSupported(remoteManifest.minimumAppVersion))
            {
                IsReady = true;
                string message = $"远端内容要求客户端 {remoteManifest.minimumAppVersion}，当前为 {Application.version}。";
                ReportStatus(message);
                return HotUpdateResult.Cached(ActiveVersion, message);
            }

            // 防止服务器部署错误导致内容版本倒退，例如本地是 1.0.2，远端却变成了 1.0.1。
            if (IsVersionOlder(remoteManifest.version, localManifest.version))
            {
                IsReady = true;
                ReportStatus($"远端版本较旧，继续使用本地版本 {ActiveVersion}。");
                return HotUpdateResult.Cached(ActiveVersion, "远端版本比本地版本旧。");
            }

            // 检查远端清单中的每个文件。文件不存在、大小不同或 Hash 不同的文件会进入下载列表。
            List<HotUpdateFileEntry> downloadList = BuildDownloadList(remoteManifest);

            // 下载列表为空，说明本地实际文件已经和远端要求完全一致。
            if (downloadList.Count == 0)
            {
                // 文件内容可能相同，但版本号等 Manifest 信息可能发生了变化，因此仍需更新本地 Manifest。
                if (!ManifestsMatch(localManifest, remoteManifest))
                {
                    WriteManifestAtomically(remoteManifest);
                }

                ActiveVersion = remoteManifest.version;
                IsReady = true;

                // 没有文件需要下载，但检查流程已经完成，所以报告 100%。
                ReportProgress(1f, 0, 0);
                ReportStatus($"内容已是最新版本 {ActiveVersion}。");
                return HotUpdateResult.Completed(ActiveVersion, "内容已经是最新版本。");
            }

            // 下载差异文件。内部负责进度、重试、临时文件、Hash 校验、统一提交和失败回滚。
            await DownloadFilesAsync(remoteBaseUrl, downloadList, timeoutSeconds);

            // 只有全部文件下载、校验并提交成功后，才写入新 Manifest。
            // Manifest 相当于“这个版本已经完整安装”的完成标记。
            WriteManifestAtomically(remoteManifest);

            ActiveVersion = remoteManifest.version;
            IsReady = true;
            ReportStatus($"热更新完成，当前内容版本 {ActiveVersion}。");
            return HotUpdateResult.Completed(ActiveVersion, "热更新完成。");
        }
        catch (Exception exception)
        {
            // 这里处理无法安全降级的问题，例如内置内容损坏、下载重试失败、Hash 不匹配或文件提交失败。
            Debug.LogError($"热更新失败：\n{exception}", this);
            ReportStatus("热更新失败，请点击登录按钮重试。");
            return HotUpdateResult.Failed(exception.Message);
        }
        finally
        {
            // 无论成功、失败或中途 return，finally 都会执行，确保后续可以再次运行更新检查。
            IsRunning = false;
        }
    }

    /// <summary>
    /// 准备一套完整可用的本地热更新内容。
    /// 优先使用 persistentDataPath 中的缓存；缓存不存在或校验失败时，从 StreamingAssets 恢复内置基线。
    /// </summary>
    /// <param name="timeoutSeconds">读取 StreamingAssets 文件时允许使用的网络请求超时时间。</param>
    /// <returns>最终可以使用的本地 Manifest。</returns>
    private async Task<HotUpdateManifest> PrepareLocalCacheAsync(int timeoutSeconds)
    {
        // 尝试从 persistentDataPath/HotUpdate/manifest.json 读取并解析缓存 Manifest。
        // 文件不存在、内容为空或格式无效时，TryReadCachedManifest 会返回 null。
        HotUpdateManifest localManifest = TryReadCachedManifest();

        // Manifest 存在时，继续按照 Manifest 中记录的路径、大小和 SHA-256 校验所有缓存文件。
        // && 具有短路特性：localManifest 为 null 时不会继续调用 ValidateCachedFiles，避免空引用异常。
        if (localManifest != null && ValidateCachedFiles(localManifest))
        {
            // Manifest 和它记录的所有缓存文件都有效，可以直接使用，不需要重新复制内置内容。
            ReportStatus($"本地缓存版本 {localManifest.version} 校验通过。");
            return localManifest;
        }

        // 执行到这里说明缓存不存在、Manifest 无效，或者至少有一个缓存文件缺失或损坏。
        // 接下来使用安装包中 StreamingAssets/HotUpdate 下的内容重新初始化缓存。
        ReportStatus("正在初始化内置热更新内容...");

        // 读取安装包内置的 manifest.json。
        // Windows 等平台可以直接读取文件；Android 等平台的 StreamingAssets 可能位于压缩包内，需要使用 UnityWebRequest 读取。
        byte[] builtInManifestBytes = await ReadBuiltInBytesAsync(HotUpdatePaths.BuiltInManifestPath, timeoutSeconds);

        // 将内置 Manifest 的 JSON 字节解析成 HotUpdateManifest，并检查版本、文件记录、路径和 Hash 格式是否合法。
        // 第二个参数“内置”用于在异常信息中标明问题来自安装包内置内容。
        HotUpdateManifest builtInManifest = ParseAndValidateManifest(builtInManifestBytes, "内置");

        // 统计内置 Manifest 中所有文件的总字节数，用于计算初始化进度。
        long totalBytes = SumFileSizes(builtInManifest.files);

        // 记录已经成功校验并写入缓存的字节数。
        long completedBytes = 0;

        // 按照内置 Manifest 中的文件记录逐个读取、校验并写入可写缓存目录。
        foreach (HotUpdateFileEntry entry in builtInManifest.files)
        {
            // 根据 Manifest 中的相对路径找到 StreamingAssets 内的文件，并异步读取全部字节。
            byte[] bytes = await ReadBuiltInBytesAsync(HotUpdatePaths.GetBuiltInFilePath(entry.path), timeoutSeconds);

            // 同时检查文件大小和 SHA-256。
            // || 具有短路特性：大小不一致时不再计算 SHA-256，可以减少一次不必要的 Hash 计算。
            if (bytes.LongLength != entry.size || !string.Equals(FileHashUtility.ComputeSha256(bytes), entry.sha256, StringComparison.OrdinalIgnoreCase))
            {
                // 内置文件不完整或内容被修改时不能继续初始化，因为无法保证得到一套可靠的本地内容。
                throw new InvalidDataException($"内置文件校验失败：{entry.path}");
            }

            // 将校验通过的文件写入 persistentDataPath/HotUpdate/files。
            // 原子写入会先写入临时文件，再替换正式文件，避免写入中断导致缓存文件损坏。
            WriteBytesAtomically(HotUpdatePaths.GetCacheFilePath(entry.path), bytes);

            // 当前文件已经成功写入缓存，将它的预期大小累计到已完成字节数中。
            completedBytes += entry.size;

            // 根据“已完成字节数 / 总字节数”计算进度，并通过事件通知登录界面。
            ReportProgress(GetProgress(completedBytes, totalBytes), completedBytes, totalBytes);
        }

        // 只有全部内置文件都读取、校验并写入成功后，才将内置 Manifest 写入缓存。
        // 这个 Manifest 表示当前缓存中已经存在一套完整的对应版本内容。
        WriteManifestAtomically(builtInManifest);

        // 通知界面内置内容初始化完成。
        ReportStatus($"内置内容版本 {builtInManifest.version} 初始化完成。");

        // 返回已经成功复制到缓存中的内置 Manifest，供后续远端版本比较和差异计算使用。
        return builtInManifest;
    }

    private HotUpdateManifest TryReadCachedManifest()
    {
        if (!File.Exists(HotUpdatePaths.CacheManifestPath))
        {
            return null;
        }

        try
        {
            return ParseAndValidateManifest(File.ReadAllBytes(HotUpdatePaths.CacheManifestPath), "本地缓存");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"读取本地热更新清单失败，将恢复内置内容。\n{exception}", this);
            return null;
        }
    }

    private static HotUpdateManifest ParseAndValidateManifest(byte[] bytes, string sourceName)
    {
        if (bytes == null || bytes.Length == 0)
        {
            throw new InvalidDataException($"{sourceName} Manifest 为空。");
        }

        HotUpdateManifest manifest = JsonUtility.FromJson<HotUpdateManifest>(System.Text.Encoding.UTF8.GetString(bytes));

        if (manifest == null || string.IsNullOrWhiteSpace(manifest.version) || manifest.files == null)
        {
            throw new InvalidDataException($"{sourceName} Manifest 格式无效。");
        }

        HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (HotUpdateFileEntry entry in manifest.files)
        {
            if (entry == null || entry.size < 0 || string.IsNullOrWhiteSpace(entry.sha256) || entry.sha256.Length != 64)
            {
                throw new InvalidDataException($"{sourceName} Manifest 包含无效文件记录。");
            }

            string normalizedPath = HotUpdatePaths.NormalizeRelativePath(entry.path);
            HotUpdatePaths.GetCacheFilePath(normalizedPath);

            if (!paths.Add(normalizedPath))
            {
                throw new InvalidDataException($"{sourceName} Manifest 包含重复路径：{normalizedPath}");
            }

            entry.path = normalizedPath;
        }

        return manifest;
    }

    private static bool ValidateCachedFiles(HotUpdateManifest manifest)
    {
        foreach (HotUpdateFileEntry entry in manifest.files)
        {
            if (!FileHashUtility.Matches(HotUpdatePaths.GetCacheFilePath(entry.path), entry.size, entry.sha256))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 根据远端 Manifest 检查本地缓存，生成本次需要下载的文件列表。
    /// </summary>
    /// <param name="manifest">已经解析并验证过的远端热更新清单。</param>
    /// <returns>本地缺失、大小不一致或 SHA-256 不一致的文件记录。</returns>
    private static List<HotUpdateFileEntry> BuildDownloadList(HotUpdateManifest manifest)
    {
        // 创建一个空列表，用于保存本次需要下载的文件记录。
        List<HotUpdateFileEntry> result = new List<HotUpdateFileEntry>();

        // 遍历远端 Manifest 中要求存在的所有文件。
        foreach (HotUpdateFileEntry entry in manifest.files)
        {
            // 根据 Manifest 中的相对路径，得到该文件在本地热更新缓存中的完整路径。
            // Matches 会依次检查文件是否存在、文件大小是否一致，以及 SHA-256 是否一致。
            // 只要其中一项不符合远端 Manifest 的要求，Matches 就会返回 false。
            if (!FileHashUtility.Matches(HotUpdatePaths.GetCacheFilePath(entry.path), entry.size, entry.sha256))
            {
                // 本地没有该文件，或者本地文件不是远端要求的版本，因此加入下载列表。
                result.Add(entry);
            }
        }

        // 返回差异文件列表。
        // 列表为空表示本地文件已经全部符合远端 Manifest，不需要下载任何文件。
        return result;
    }

    /// <summary>
    /// 下载差异文件列表中的所有文件，并在全部下载和校验成功后统一提交到正式缓存目录。
    /// </summary>
    /// <param name="remoteBaseUrl">远端热更新根地址。</param>
    /// <param name="downloadList">本次需要下载的文件记录。</param>
    /// <param name="timeoutSeconds">每个文件网络请求的超时时间。</param>
    private async Task DownloadFilesAsync(string remoteBaseUrl, List<HotUpdateFileEntry> downloadList, int timeoutSeconds)
    {
        // 统计本次所有待下载文件的总字节数，用于计算整体下载进度。
        long totalBytes = SumFileSizes(downloadList);

        // 记录已经完整下载并校验通过的文件字节数。
        long completedBytes = 0;

        // Staging 是更新文件的暂存目录。
        // 新文件不会直接覆盖正式缓存，避免下载过程中断导致当前可用版本损坏。
        string stagingRoot = Path.Combine(HotUpdatePaths.CacheRoot, "Staging");

        // 删除上一次更新可能遗留的 Staging 目录，然后重新创建一个干净的暂存目录。
        RecreateDirectory(stagingRoot);

        // 通知登录界面本次需要下载的文件数量和总大小。
        ReportStatus($"发现 {downloadList.Count} 个更新文件，共 {FormatBytes(totalBytes)}。");

        // 下载开始前报告一次 0% 进度。
        ReportProgress(0f, 0, totalBytes);

        // 当前实现按照下载列表顺序逐个下载文件，不会并行下载。
        foreach (HotUpdateFileEntry entry in downloadList)
        {
            // 根据文件的相对路径生成它在 Staging 目录中的安全完整路径。
            // GetSafePath 会阻止使用 ../ 等路径跳出 Staging 目录。
            string stagedPath = HotUpdatePaths.GetSafePath(stagingRoot, entry.path);

            // 下载过程中先写入 .tmp 临时文件。
            // 只有下载完成并且大小、Hash 校验通过后，才会去掉 .tmp 后缀。
            string temporaryPath = stagedPath + ".tmp";

            // 确保当前文件对应的父目录存在，例如 Staging/Lua/Skill。
            Directory.CreateDirectory(Path.GetDirectoryName(stagedPath));

            // 拼接文件的完整下载地址，例如：
            // http://127.0.0.1:8000/HotUpdate/Windows/files/Lua/Skill/SkillConfig.lua
            string fileUrl = CombineUrl(CombineUrl(remoteBaseUrl, HotUpdatePaths.FilesDirectoryName), entry.path);

            // 保存当前文件最后一次下载失败时产生的异常。
            // 如果最终下载成功，会重新设置为 null。
            Exception lastException = null;

            // 当前文件最多尝试下载 MaximumDownloadAttempts 次。
            for (int attempt = 1; attempt <= MaximumDownloadAttempts; attempt++)
            {
                try
                {
                    // 开始新一轮下载前删除上一次失败可能遗留的临时文件。
                    DeleteIfExists(temporaryPath);

                    // 通知界面当前下载的文件以及当前是第几次尝试。
                    ReportStatus($"正在下载 {entry.path}（{attempt}/{MaximumDownloadAttempts}）");

                    // 将远端文件直接下载到 .tmp 文件。
                    // completedBytes 和 totalBytes 用于在下载当前文件时计算整个更新任务的总体进度。
                    await DownloadFileAsync(fileUrl, temporaryPath, timeoutSeconds, completedBytes, totalBytes, entry.size);

                    // 下载完成后检查临时文件的实际大小和 SHA-256 是否符合远端 Manifest。
                    // 即使网络请求成功，文件内容不完整或被修改，也会被视为下载失败。
                    if (!FileHashUtility.Matches(temporaryPath, entry.size, entry.sha256))
                    {
                        throw new InvalidDataException($"下载文件 Hash 或大小不匹配：{entry.path}");
                    }

                    // 文件校验通过后，将 .tmp 文件移动为 Staging 中的正式暂存文件。
                    // 此时文件仍未进入真正的热更新缓存目录。
                    File.Move(temporaryPath, stagedPath);

                    // 当前文件已经完整下载并校验成功，将它的大小累计到已完成字节数中。
                    completedBytes += entry.size;

                    // 报告当前文件完成后的准确整体进度。
                    ReportProgress(GetProgress(completedBytes, totalBytes), completedBytes, totalBytes);

                    // 清除之前尝试留下的异常，表示当前文件最终下载成功。
                    lastException = null;

                    // 当前文件已经成功，不再继续执行剩余重试次数。
                    break;
                }
                catch (Exception exception)
                {
                    // 保存当前这一次下载失败的异常。
                    lastException = exception;

                    // 删除下载失败、校验失败或下载中断后留下的不完整临时文件。
                    DeleteIfExists(temporaryPath);

                    // 在 Unity Console 中记录文件、尝试次数和具体失败原因。
                    Debug.LogWarning($"下载失败：{entry.path}，第 {attempt} 次尝试。\n{exception.Message}", this);

                    // 如果还没有达到最大尝试次数，就等待一段时间后继续重试。
                    if (attempt < MaximumDownloadAttempts)
                    {
                        // 使用简单的指数退避：
                        // 第一次失败等待 1000 * 1 = 1 秒；
                        // 第二次失败等待 1000 * 2 = 2 秒；
                        // 最后一次失败后不再等待。
                        await Task.Delay(1000 * (1 << (attempt - 1)));
                    }
                }
            }

            // 重试循环结束后 lastException 仍然不为 null，说明当前文件所有下载尝试都失败了。
            if (lastException != null)
            {
                // 抛出异常并停止整个更新流程，不再下载后续文件，也不会提交 Staging 中的任何新文件。
                // lastException 作为内部异常保留下来，方便查看最底层的真实失败原因。
                throw new IOException($"文件 {entry.path} 下载 {MaximumDownloadAttempts} 次后仍然失败。", lastException);
            }
        }

        // 执行到这里说明所有文件都已经下载完成，并且大小和 SHA-256 校验全部通过。
        // 将 Staging 中的新文件统一提交到正式缓存目录，提交失败时会使用 Backup 回滚。
        CommitStagedFiles(stagingRoot, downloadList);

        // 所有新文件已经成功进入正式缓存，删除不再需要的 Staging 目录及其内容。
        Directory.Delete(stagingRoot, true);
    }
    private static void CommitStagedFiles(string stagingRoot, List<HotUpdateFileEntry> downloadList)
    {
        string backupRoot = Path.Combine(HotUpdatePaths.CacheRoot, "Backup");
        RecreateDirectory(backupRoot);
        List<HotUpdateFileEntry> committedEntries = new List<HotUpdateFileEntry>();

        try
        {
            foreach (HotUpdateFileEntry entry in downloadList)
            {
                string stagedPath = HotUpdatePaths.GetSafePath(stagingRoot, entry.path);
                string targetPath = HotUpdatePaths.GetCacheFilePath(entry.path);
                string backupPath = HotUpdatePaths.GetSafePath(backupRoot, entry.path);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath));

                if (File.Exists(targetPath))
                {
                    File.Move(targetPath, backupPath);
                }

                try
                {
                    File.Move(stagedPath, targetPath);
                    committedEntries.Add(entry);
                }
                catch
                {
                    if (File.Exists(backupPath))
                    {
                        File.Move(backupPath, targetPath);
                    }

                    throw;
                }
            }
        }
        catch
        {
            for (int i = committedEntries.Count - 1; i >= 0; i--)
            {
                HotUpdateFileEntry entry = committedEntries[i];
                string targetPath = HotUpdatePaths.GetCacheFilePath(entry.path);
                string backupPath = HotUpdatePaths.GetSafePath(backupRoot, entry.path);
                DeleteIfExists(targetPath);

                if (File.Exists(backupPath))
                {
                    File.Move(backupPath, targetPath);
                }
            }

            throw;
        }
        finally
        {
            if (Directory.Exists(backupRoot))
            {
                Directory.Delete(backupRoot, true);
            }
        }
    }

    /// <summary>
    /// 将一个远端文件异步下载到指定的临时文件，并在下载过程中报告整体更新进度。
    /// </summary>
    /// <param name="url">文件的完整下载地址。</param>
    /// <param name="temporaryPath">下载内容写入的本地 .tmp 临时文件路径。</param>
    /// <param name="timeoutSeconds">本次网络请求的超时时间。</param>
    /// <param name="completedBytes">下载当前文件之前，已经完成的其他文件字节数。</param>
    /// <param name="totalBytes">本次更新需要下载的所有文件总字节数。</param>
    /// <param name="currentFileSize">远端 Manifest 中记录的当前文件大小。</param>
    private async Task DownloadFileAsync(string url, string temporaryPath, int timeoutSeconds, long completedBytes, long totalBytes, long currentFileSize)
    {
        // 创建一个使用 HTTP GET 方法的 UnityWebRequest。
        // using 声明确保方法结束时自动释放 UnityWebRequest 占用的网络和原生资源。
        using UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbGET);

        // 将服务器返回的数据直接写入磁盘上的临时文件，而不是把整个文件保存在内存中。
        // removeFileOnAbort 为 true，表示请求被中止时自动删除未下载完整的临时文件。
        request.downloadHandler = new DownloadHandlerFile(temporaryPath) { removeFileOnAbort = true };

        // 设置请求超时时间，单位是秒。
        // Mathf.Max 保证超时时间最少为 1 秒，防止传入 0 或负数。
        request.timeout = Mathf.Max(1, timeoutSeconds);

        // 正式发送请求，并取得用于观察请求执行状态的异步操作对象。
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();

        // 请求没有完成时，每一帧读取当前下载量并更新整体进度。
        while (!operation.isDone)
        {
            // request.downloadedBytes 是当前文件已经收到的字节数。
            // Math.Min 防止网络组件报告的字节数意外超过 Manifest 中记录的文件大小。
            long currentBytes = Math.Min((long)request.downloadedBytes, currentFileSize);

            // 整体已下载字节数 = 之前已经完成的文件字节数 + 当前文件已下载字节数。
            long downloadedBytes = completedBytes + currentBytes;

            // 根据整体已下载字节数和总字节数计算进度，并通知登录界面更新显示。
            ReportProgress(GetProgress(downloadedBytes, totalBytes), downloadedBytes, totalBytes);

            // 暂停当前方法，把执行权交还给 Unity，使游戏可以继续渲染和处理其他逻辑。
            // 下一帧左右会从这里恢复执行，再次检查 operation.isDone。
            await Task.Yield();
        }

        // operation.isDone 只表示请求已经结束，不代表请求一定成功。
        // 网络超时、无法连接、404 或服务器错误等情况也会让请求结束。
        if (request.result != UnityWebRequest.Result.Success)
        {
            // 将 UnityWebRequest 的错误信息包装成 IOException，交给上层执行重试逻辑。
            throw new IOException($"请求失败：{url}，{request.error}");
        }

        // 执行到这里表示网络请求成功。
        // 当前方法只负责下载文件；文件大小和 SHA-256 校验由上层 DownloadFilesAsync 负责。
    }

    private static async Task<byte[]> DownloadBytesAsync(string url, int timeoutSeconds)
    {
        using UnityWebRequest request = UnityWebRequest.Get(url);
        request.timeout = Mathf.Max(1, timeoutSeconds);
        UnityWebRequestAsyncOperation operation = request.SendWebRequest();

        while (!operation.isDone)
        {
            await Task.Yield();
        }

        if (request.result != UnityWebRequest.Result.Success)
        {
            throw new IOException($"请求失败：{url}，{request.error}");
        }

        return request.downloadHandler.data;
    }

    private static Task<byte[]> ReadBuiltInBytesAsync(string path, int timeoutSeconds)
    {
        if (File.Exists(path))
        {
            return Task.FromResult(File.ReadAllBytes(path));
        }

        return DownloadBytesAsync(ToRequestUrl(path), timeoutSeconds);
    }

    private static string ToRequestUrl(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out Uri uri) && !string.IsNullOrEmpty(uri.Scheme))
        {
            return uri.AbsoluteUri;
        }

        return new Uri(Path.GetFullPath(path)).AbsoluteUri;
    }

    private static string CombineUrl(string baseUrl, string relativePath)
    {
        string[] parts = HotUpdatePaths.NormalizeRelativePath(relativePath).Split('/');

        for (int i = 0; i < parts.Length; i++)
        {
            parts[i] = Uri.EscapeDataString(parts[i]);
        }

        return baseUrl.TrimEnd('/') + "/" + string.Join("/", parts);
    }

    private static bool IsApplicationVersionSupported(string minimumVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumVersion) || !Version.TryParse(minimumVersion, out Version requiredVersion))
        {
            return true;
        }

        return !Version.TryParse(Application.version, out Version applicationVersion) || applicationVersion >= requiredVersion;
    }

    private static bool IsVersionOlder(string candidateVersion, string currentVersion)
    {
        return Version.TryParse(candidateVersion, out Version candidate) && Version.TryParse(currentVersion, out Version current) && candidate < current;
    }

    private static bool ManifestsMatch(HotUpdateManifest left, HotUpdateManifest right)
    {
        if (!string.Equals(left.version, right.version, StringComparison.Ordinal) || left.files.Length != right.files.Length)
        {
            return false;
        }

        Dictionary<string, HotUpdateFileEntry> leftFiles = new Dictionary<string, HotUpdateFileEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (HotUpdateFileEntry entry in left.files)
        {
            leftFiles[entry.path] = entry;
        }

        foreach (HotUpdateFileEntry entry in right.files)
        {
            if (!leftFiles.TryGetValue(entry.path, out HotUpdateFileEntry leftEntry) || leftEntry.size != entry.size ||
                !string.Equals(leftEntry.sha256, entry.sha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static void WriteManifestAtomically(HotUpdateManifest manifest)
    {
        string json = JsonUtility.ToJson(manifest, true);
        WriteBytesAtomically(HotUpdatePaths.CacheManifestPath, System.Text.Encoding.UTF8.GetBytes(json));
    }

    private static void WriteBytesAtomically(string targetPath, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
        string temporaryPath = targetPath + ".tmp";
        DeleteIfExists(temporaryPath);
        File.WriteAllBytes(temporaryPath, bytes);
        ReplaceFileAtomically(temporaryPath, targetPath);
    }

    private static void ReplaceFileAtomically(string temporaryPath, string targetPath)
    {
        string backupPath = targetPath + ".bak";
        DeleteIfExists(backupPath);

        if (!File.Exists(targetPath))
        {
            File.Move(temporaryPath, targetPath);
            return;
        }

        File.Move(targetPath, backupPath);

        try
        {
            File.Move(temporaryPath, targetPath);
            DeleteIfExists(backupPath);
        }
        catch
        {
            DeleteIfExists(targetPath);
            File.Move(backupPath, targetPath);
            throw;
        }
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static void RecreateDirectory(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, true);
        }

        Directory.CreateDirectory(directoryPath);
    }

    private static long SumFileSizes(IEnumerable<HotUpdateFileEntry> files)
    {
        long total = 0;

        foreach (HotUpdateFileEntry file in files)
        {
            total += Math.Max(0, file.size);
        }

        return total;
    }

    private static float GetProgress(long downloadedBytes, long totalBytes)
    {
        return totalBytes > 0 ? Mathf.Clamp01((float)downloadedBytes / totalBytes) : 1f;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024f * 1024f):0.00} MB";
        }

        if (bytes >= 1024)
        {
            return $"{bytes / 1024f:0.00} KB";
        }

        return $"{bytes} B";
    }

    private void ReportStatus(string message)
    {
        StatusChanged?.Invoke(message);
        Debug.Log($"[HotUpdate] {message}", this);
    }

    private void ReportProgress(float progress, long downloadedBytes, long totalBytes)
    {
        ProgressChanged?.Invoke(progress, downloadedBytes, totalBytes);
    }
}
