using UnityEngine;

public class GameSettingsManager : MonoBehaviour
{
    public static GameSettingsManager Instance
    {
        get;
        private set;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError(
                "场景中存在多个 GameSettingsManager。",
                this
            );

            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        // SaveManager已经在Awake中完成存档加载。
        ApplySavedSettings();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 启动游戏时应用存档中的非音频设置。
    /// </summary>
    public void ApplySavedSettings()
    {
        SettingsSaveData settings = GetSettings();

        if (settings == null)
        {
            return;
        }

        ApplyQualityLevel(settings.qualityLevel);
    }

    /// <summary>
    /// 设置页面选择画质时调用。
    /// </summary>
    public void SetQualityLevel(int qualityLevel)
    {
        SettingsSaveData settings = GetSettings();

        if (settings == null)
        {
            return;
        }

        int validLevel =
            GetValidQualityLevel(qualityLevel);

        settings.qualityLevel = validLevel;

        ApplyQualityLevel(validLevel);
    }

    private void ApplyQualityLevel(int qualityLevel)
    {
        if (QualitySettings.names.Length == 0)
        {
            return;
        }

        int validLevel =
            GetValidQualityLevel(qualityLevel);

        // true表示立即应用阴影、贴图等较昂贵的画质变化。
        QualitySettings.SetQualityLevel(
            validLevel,
            true
        );
    }

    private static int GetValidQualityLevel(
        int qualityLevel)
    {
        int highestLevel = Mathf.Max(
            0,
            QualitySettings.names.Length - 1
        );

        return Mathf.Clamp(
            qualityLevel,
            0,
            highestLevel
        );
    }

    private static SettingsSaveData GetSettings()
    {
        if (GameEntry.Instance == null ||
            GameEntry.Save == null ||
            GameEntry.Save.Data == null)
        {
            return null;
        }

        return GameEntry.Save.Data.settings;
    }
}