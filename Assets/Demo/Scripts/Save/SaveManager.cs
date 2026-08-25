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
        if(!File.Exists(savePath))
        {
            CreateNewSave();
            return;
        }


        try
        {
            // 读取 JSON 字符串。
            string json = File.ReadAllText(savePath);

            // 先创建默认数据。
            // 这样旧存档缺少新字段时，可以保留默认值。
            Data = new SaveData();

            // 使用覆盖方式读取数据。
            // JSON 中存在的字段会覆盖默认值，
            // 不存在的字段保持默认值。
            JsonUtility.FromJsonOverwrite(json, Data);

            CheckVersion();
        }
        catch
        {
            // 存档损坏时重新创建。
            Debug.LogError("读取存档失败，重新创建存档。");

            CreateNewSave();
        }
    }


    /// <summary>
    /// 创建新的默认存档。
    /// </summary>
    private void CreateNewSave()
    {
        Data = new SaveData();

        Save();
    }


    /// <summary>
    /// 保存当前数据到 JSON 文件。
    /// </summary>
    public void Save()
    {
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
    /// 检查存档版本。
    /// </summary>
    private void CheckVersion()
    {
        if(Data.version < CurrentVersion)
        {
            Migrate(Data.version, CurrentVersion);
        }
    }


    /// <summary>
    /// 存档版本迁移。
    /// 当存档结构发生变化时，在这里处理旧版本数据。
    /// </summary>
    private void Migrate(int oldVersion, int newVersion)
    {
        Debug.Log($"存档迁移 {oldVersion} -> {newVersion}");

        Data.version = newVersion;

        Save();
    }
}