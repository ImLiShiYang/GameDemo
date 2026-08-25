using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;
using System.Collections.Generic;

public sealed class SettingsOpenArgs
{
    public Action OnClosed;
}

public class SettingsPanel : UIBase
{
    [Header("Volume Sliders")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;

    [Header("Mute Toggles")]
    [SerializeField] private Toggle masterMuteToggle;
    [SerializeField] private Toggle musicMuteToggle;
    [SerializeField] private Toggle sfxMuteToggle;

    [Header("Buttons")]
    [SerializeField] private Button backButton;
    
    [Header("Quality")]
    [SerializeField] private TMP_Dropdown qualityDropdown;
    
    private SettingsOpenArgs currentArgs;

    protected override void OnInit()
    {
        PopulateQualityOptions();
        
        masterSlider.onValueChanged.AddListener(HandleMasterVolumeChanged);
        musicSlider.onValueChanged.AddListener(HandleMusicVolumeChanged);
        sfxSlider.onValueChanged.AddListener(HandleSfxVolumeChanged);

        masterMuteToggle.onValueChanged.AddListener(HandleMasterMutedChanged);
        musicMuteToggle.onValueChanged.AddListener(HandleMusicMutedChanged);
        sfxMuteToggle.onValueChanged.AddListener(HandleSfxMutedChanged);

        backButton.onClick.AddListener(CloseSelf);
        
        qualityDropdown.onValueChanged.AddListener(HandleQualityLevelChanged);
    }

    private void PopulateQualityOptions()
    {
        qualityDropdown.ClearOptions();

        List<string> localizedNames =
            new List<string>();

        foreach (string qualityName in QualitySettings.names)
        {
            localizedNames.Add(
                GetLocalizedQualityName(qualityName)
            );
        }

        qualityDropdown.AddOptions(localizedNames);
    }
    
    private static string GetLocalizedQualityName(string qualityName)
    {
        switch (qualityName)
        {
            case "Performant":
                return "性能优先";

            case "Balanced":
                return "均衡";

            case "High Fidelity":
                return "高画质";

            default:
                // 遇到没有配置中文名称的新画质等级时，
                // 继续显示项目中的原始名称。
                return qualityName;
        }
    }
    
    protected override void OnOpen(object args)
    {
        currentArgs = args as SettingsOpenArgs;
        RefreshControls();
    }

    protected override void OnRefresh(object args)
    {
        if (args is SettingsOpenArgs openArgs)
        {
            currentArgs = openArgs;
        }

        RefreshControls();
    }

    protected override void OnClose()
    {
        if (GameEntry.Save != null)
        {
            GameEntry.Save.Save();
        }

        SettingsOpenArgs closedArgs = currentArgs;
        currentArgs = null;

        closedArgs?.OnClosed?.Invoke();
    }

    private void RefreshControls()
    {
        if (GameEntry.Save == null ||
            GameEntry.Save.Data == null ||
            GameEntry.Save.Data.settings == null)
        {
            return;
        }

        SettingsSaveData settings = GameEntry.Save.Data.settings;

        // 不触发监听事件，只刷新控件显示。
        masterSlider.SetValueWithoutNotify(settings.masterVolume);
        musicSlider.SetValueWithoutNotify(settings.musicVolume);
        sfxSlider.SetValueWithoutNotify(settings.sfxVolume);

        masterMuteToggle.SetIsOnWithoutNotify(settings.masterMuted);
        musicMuteToggle.SetIsOnWithoutNotify(settings.musicMuted);
        sfxMuteToggle.SetIsOnWithoutNotify(settings.sfxMuted);
        
        int highestQualityLevel = Mathf.Max(
            0,
            QualitySettings.names.Length - 1
        );

        int validQualityLevel = Mathf.Clamp(
            settings.qualityLevel,
            0,
            highestQualityLevel
        );

        qualityDropdown.SetValueWithoutNotify(
            validQualityLevel
        );

        qualityDropdown.RefreshShownValue();
    }
    
    private void HandleQualityLevelChanged(int qualityLevel)
    {
        GameSettingsManager.Instance?.SetQualityLevel(
            qualityLevel
        );
    }

    private void HandleMasterVolumeChanged(float value)
    {
        GameAudioManager.Instance?.SetMasterVolume(value);
    }

    private void HandleMusicVolumeChanged(float value)
    {
        GameAudioManager.Instance?.SetMusicVolume(value);
    }

    private void HandleSfxVolumeChanged(float value)
    {
        GameAudioManager.Instance?.SetSfxVolume(value);
    }

    private void HandleMasterMutedChanged(bool muted)
    {
        GameAudioManager.Instance?.SetMasterMuted(muted);
    }

    private void HandleMusicMutedChanged(bool muted)
    {
        GameAudioManager.Instance?.SetMusicMuted(muted);
    }

    private void HandleSfxMutedChanged(bool muted)
    {
        GameAudioManager.Instance?.SetSfxMuted(muted);
    }
}