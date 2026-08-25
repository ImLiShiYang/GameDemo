using System.IO;
using UnityEngine;


/// 游戏存档管理器。
/// 负责：
// — 创建存档
// — 读取存档
// — 保存存档
// — 存档版本检测
// — 版本迁移
public class SaveManager : MonoBehaviour
{
    // 当前存档版本号。
    // 后续修改存档结构时，通过版本号进行兼容处理。
    private const int CurrentVersion = 1;

    // 存档文件完整路径。
    private string savePath;

    private bool canWriteSave = true;

    // 当前加载后的存档数据。
    public SaveData Data
    {
        get;
        private set;
    }


    private void Awake()
    {
        // 获取持久化存档路径。
        // Windows 下通常位于 AppData/LocalLow。
        savePath = Path.Combine(Application.persistentDataPath, "save.json");

        Load();
    }


    /// <summary>
    /// 加载存档。
    /// 如果存档不存在，则创建新的默认存档。
    /// </summary>
    public void Load()
    {
        // 每次重新加载前，先恢复默认的可写状态。
        // 如果后面发现是未来版本，再切换成只读。
        canWriteSave = true;

        if (!File.Exists(savePath))
        {
            CreateNewSave();
            return;
        }

        try
        {
            string json = File.ReadAllText(savePath);

            // 先创建带有默认值的数据对象。
            Data = new SaveData();

            // JSON中缺少的字段继续使用默认值。
            JsonUtility.FromJsonOverwrite(json, Data);

            // 检查版本并执行迁移。
            bool versionIsSupported =
                TryMigrateToCurrentVersion(
                    out bool versionWasMigrated
                );

            // 未来版本存档进入只读状态。
            // 必须在后面可能调用Save之前赋值。
            canWriteSave = versionIsSupported;

            // 补齐对象并修复非法数据。
            bool dataWasRepaired =
                ValidateAndRepair();

            // 只有支持当前版本时才能写回修复结果。
            if (versionIsSupported &&
                (versionWasMigrated || dataWasRepaired))
            {
                Save();
            }
        }
        catch (System.Exception exception)
        {
            Debug.LogError(
                $"读取存档失败，将创建默认存档。\n" +
                $"存档路径：{savePath}\n" +
                $"异常信息：{exception}",
                this
            );

            // 损坏存档会被默认存档替换，因此恢复可写。
            canWriteSave = true;
            CreateNewSave();
        }
    }

    /// <summary>
    /// 补齐可能为空的数据对象，并修复越界数值。
    /// 返回 true 表示数据被修改过，需要重新保存。
    /// </summary>
    private bool ValidateAndRepair()
    {
        bool repaired = false;

        // 防止旧存档中 player 字段缺失或被写成 null。
        if(Data.player == null)
        {
            Data.player = new PlayerSaveData();
            repaired = true;
        }

        // 防止旧存档中 settings 字段缺失或被写成 null。
        if(Data.settings == null)
        {
            Data.settings = new SettingsSaveData();
            repaired = true;
        }

        // 最高分不能小于 0。
        int validHighestScore = Mathf.Max(
            0,
            Data.player.highestScore
        );

        if(Data.player.highestScore != validHighestScore)
        {
            Data.player.highestScore = validHighestScore;
            repaired = true;
        }

        // 三类音量都限制在 0～1。
        repaired |= RepairVolume(
            ref Data.settings.masterVolume
        );

        repaired |= RepairVolume(
            ref Data.settings.musicVolume
        );

        repaired |= RepairVolume(
            ref Data.settings.sfxVolume
        );

        // 获取当前项目允许使用的最高画质索引。
        int highestQualityLevel = Mathf.Max(
            0,
            QualitySettings.names.Length - 1
        );

        int validQualityLevel = Mathf.Clamp(
            Data.settings.qualityLevel,
            0,
            highestQualityLevel
        );

        if(Data.settings.qualityLevel != validQualityLevel)
        {
            Data.settings.qualityLevel = validQualityLevel;
            repaired = true;
        }

        return repaired;
    }

    /// <summary>
    /// 将音量限制在 0～1。
    /// NaN 和无穷值恢复成默认音量 1。
    /// </summary>
    private static bool RepairVolume(ref float volume)
    {
        float validVolume =
            float.IsNaN(volume) ||
            float.IsInfinity(volume)
                ? 1f
                : Mathf.Clamp01(volume);

        // 数值没有变化，不需要重新保存。
        if(Mathf.Approximately(volume, validVolume))
        {
            return false;
        }

        volume = validVolume;
        return true;
    }
    
    /// <summary>
    /// 创建新的默认存档。
    /// </summary>
    private void CreateNewSave()
    {
        canWriteSave = true;
        Data = new SaveData();
        Save();
    }


    /// <summary>
    /// 保存当前数据到 JSON 文件。
    /// </summary>
    public void Save()
    {
        if (!canWriteSave)
        {
            Debug.LogWarning("当前存档版本高于客户端版本，已阻止保存。", this);
            return;
        }
        
        if(Data == null)
        {
            return;
        }

        // 保存前统一设置当前版本。
        Data.version = CurrentVersion;

        // 转换成格式化 JSON。
        string json = JsonUtility.ToJson(Data, true);

        // 写入文件。
        File.WriteAllText(savePath, json);
    }


    /// <summary>
    /// 将旧存档逐版本迁移到当前版本。
    /// 返回 false 表示存档来自更高版本的客户端，
    /// 当前客户端只能读取，不能覆盖保存。
    /// </summary>
    private bool TryMigrateToCurrentVersion(out bool versionWasMigrated)
    {
        versionWasMigrated = false;

        // 存档版本比当前客户端更新。
        // 继续保存会造成版本降级和字段丢失，因此禁止写回。
        if(Data.version > CurrentVersion)
        {
            Debug.LogWarning(
                $"存档版本高于当前客户端版本，" +
                $"将跳过存档写回。" +
                $"存档版本：{Data.version}，" +
                $"客户端版本：{CurrentVersion}",
                this
            );

            return false;
        }

        // 必须逐版本迁移，不能直接把版本号改成最新值。
        while(Data.version < CurrentVersion)
        {
            switch(Data.version)
            {
                case 0:
                    MigrateVersion0To1();
                    Data.version = 1;
                    versionWasMigrated = true;
                    break;

                default:
                    throw new InvalidDataException(
                        $"没有找到存档版本 {Data.version} " +
                        $"到版本 {Data.version + 1} 的迁移方法。"
                    );
            }
        }

        return true;
    }
    
    /// <summary>
    /// version 0 代表还没有 version 字段的早期存档。
    /// version 1 正式加入 player 和 settings 数据结构。
    /// </summary>
    private void MigrateVersion0To1()
    {
        if(Data.player == null)
        {
            Data.player = new PlayerSaveData();
        }

        if(Data.settings == null)
        {
            Data.settings = new SettingsSaveData();
        }

        Debug.Log(
            "存档已经从 version 0 迁移到 version 1。",
            this
        );
    }

}