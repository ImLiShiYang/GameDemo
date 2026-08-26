using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 统一管理 AudioMixer、背景音乐和音频设置。
/// </summary>
[DefaultExecutionOrder(-100)]
public class GameAudioManager : MonoBehaviour
{
    private const string MasterVolumeParameter = "MasterVolume";

    private const string MusicVolumeParameter = "MusicVolume";

    private const string SfxVolumeParameter = "SFXVolume";

    private const float MutedDecibels = -80f;

    private const string NormalMusicAddress = "Audio/Music/BattleNormal";

    private const string BossMusicAddress = "Audio/Music/BattleBoss";

    public static GameAudioManager Instance
    {
        get;
        private set;
    }

    [Header("Audio Mixer")]
    [SerializeField]
    private AudioMixer audioMixer;

    [SerializeField]
    private AudioMixerGroup musicMixerGroup;

    [SerializeField]
    private AudioMixerGroup sfxMixerGroup;

    [Header("Music Source")]
    [SerializeField]
    private AudioSource musicSource;

    private WaveManager waveManager;
    private Health currentBossHealth;
    private readonly Dictionary<string, AudioClip> musicCache = new();
    private string currentMusicAddress;
    private int musicRequestVersion;
    private bool isDestroying;

    public AudioMixerGroup SfxMixerGroup => sfxMixerGroup;

    private void Awake()
    {
        if(Instance != null && Instance != this)
        {
            Debug.LogError("场景中存在多个 GameAudioManager。", this);

            Destroy(gameObject);
            return;
        }

        Instance = this;

        PrepareMusicSource();
    }
    
    public void PauseMusic()
    {
        if (musicSource != null)
        {
            musicSource.Pause();
        }
    }

    public void ResumeMusic()
    {
        if (musicSource != null)
        {
            musicSource.UnPause();
        }
    }

    private void Start()
    {
        ApplySavedSettings();
        BindWaveManager();
        PlayNormalMusic();
    }

    private void OnDestroy()
    {
        isDestroying = true;
        musicRequestVersion++;

        if(musicSource != null)
        {
            musicSource.Stop();
            musicSource.clip = null;
        }

        AddressableResourceManager resourceManager = AddressableResourceManager.Instance;

        if(resourceManager != null)
        {
            foreach(string address in musicCache.Keys)
            {
                resourceManager.ReleaseAudio(address);
            }
        }

        musicCache.Clear();
        currentMusicAddress = null;

        UnbindBoss();

        if(waveManager != null)
        {
            waveManager.BossSpawned -= HandleBossSpawned;
            waveManager.Victory -= HandleVictory;
        }

        if(Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 确保背景音乐 AudioSource 使用 Music 分组。
    /// </summary>
    private void PrepareMusicSource()
    {
        if(musicSource == null)
        {
            musicSource = GetComponent<AudioSource>();
        }

        if(musicSource == null)
        {
            musicSource = gameObject.AddComponent<AudioSource>();
        }

        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.outputAudioMixerGroup = musicMixerGroup;
    }

    /// <summary>
    /// 监听 Boss 出现和通关事件。
    /// </summary>
    private void BindWaveManager()
    {
        if(GameEntry.Instance == null)
        {
            return;
        }

        waveManager = GameEntry.Wave;

        if(waveManager == null)
        {
            return;
        }

        waveManager.BossSpawned += HandleBossSpawned;
        waveManager.Victory += HandleVictory;
    }

    private void HandleBossSpawned(
        string bossName,
        Health bossHealth)
    {
        UnbindBoss();

        currentBossHealth = bossHealth;

        if(currentBossHealth != null)
        {
            currentBossHealth.Died += HandleBossDied;
        }

        PlayBossMusic();
    }

    private void HandleBossDied()
    {
        UnbindBoss();
        PlayNormalMusic();
    }

    private void HandleVictory()
    {
        UnbindBoss();
        PlayNormalMusic();
    }

    private void UnbindBoss()
    {
        if(currentBossHealth != null)
        {
            currentBossHealth.Died -= HandleBossDied;
            currentBossHealth = null;
        }
    }

    public void PlayNormalMusic()
    {
        PlayMusic(NormalMusicAddress);
    }

    public void PlayBossMusic()
    {
        PlayMusic(BossMusicAddress);
    }

    private async void PlayMusic(string address)
    {
        try
        {
            await PlayMusicAsync(address);
        }
        catch(Exception exception)
        {
            Debug.LogError($"异步切换背景音乐失败：{address}\n{exception}", this);
        }
    }

    private async Task PlayMusicAsync(string address)
    {
        if(isDestroying || musicSource == null || string.IsNullOrEmpty(address))
        {
            return;
        }

        if(currentMusicAddress == address && musicSource.clip != null && musicSource.isPlaying)
        {
            return;
        }

        AddressableResourceManager resourceManager = AddressableResourceManager.Instance;

        if(resourceManager == null)
        {
            throw new InvalidOperationException("场景中没有 AddressableResourceManager，无法加载背景音乐。");
        }

        int requestVersion = ++musicRequestVersion;

        if(!musicCache.TryGetValue(address, out AudioClip loadedClip))
        {
            AudioClip requestedClip = await resourceManager.LoadAudioAsync(address);

            if(this == null || isDestroying)
            {
                resourceManager.ReleaseAudio(address);
                return;
            }

            // 两个并发请求可能同时等待同一个 Handle。后完成的请求归还多取的一次引用。
            if(musicCache.TryGetValue(address, out loadedClip))
            {
                resourceManager.ReleaseAudio(address);
            }
            else
            {
                loadedClip = requestedClip;
                musicCache[address] = loadedClip;
            }
        }
        else
        {
            Debug.Log($"使用已缓存音乐：{address}", this);
        }

        // 加载过程中可能已经请求了另一首音乐，或者当前 GameAudioManager 已被销毁。
        // 过期请求不能覆盖新音乐，但资源会留在本地缓存供后续切换复用。
        if(this == null || isDestroying || requestVersion != musicRequestVersion)
        {
            return;
        }

        musicSource.Stop();
        musicSource.clip = loadedClip;
        currentMusicAddress = address;
        musicSource.Play();

        Debug.Log($"开始播放 Addressables 音乐：{address}", this);
    }

    /// <summary>
    /// 将存档中的三个音量和静音状态应用到 Mixer。
    /// </summary>
    public void ApplySavedSettings()
    {
        SettingsSaveData settings = GetSettings();

        if(settings == null)
        {
            return;
        }

        ApplyMixerVolume(MasterVolumeParameter, settings.masterVolume, settings.masterMuted);

        ApplyMixerVolume(MusicVolumeParameter, settings.musicVolume, settings.musicMuted);

        ApplyMixerVolume(SfxVolumeParameter, settings.sfxVolume, settings.sfxMuted);
    }

    public void SetMasterVolume(float value)
    {
        SettingsSaveData settings = GetSettings();

        if(settings == null)
        {
            return;
        }

        settings.masterVolume = Mathf.Clamp01(value);

        ApplyMixerVolume(MasterVolumeParameter, settings.masterVolume, settings.masterMuted);
    }

    public void SetMusicVolume(float value)
    {
        SettingsSaveData settings = GetSettings();

        if(settings == null)
        {
            return;
        }

        settings.musicVolume = Mathf.Clamp01(value);

        ApplyMixerVolume(MusicVolumeParameter, settings.musicVolume, settings.musicMuted);
    }

    public void SetSfxVolume(float value)
    {
        SettingsSaveData settings = GetSettings();

        if(settings == null)
        {
            return;
        }

        settings.sfxVolume = Mathf.Clamp01(value);

        ApplyMixerVolume(SfxVolumeParameter, settings.sfxVolume, settings.sfxMuted);
    }

    public void SetMasterMuted(bool muted)
    {
        SettingsSaveData settings = GetSettings();

        if(settings == null)
        {
            return;
        }

        settings.masterMuted = muted;

        ApplyMixerVolume(MasterVolumeParameter, settings.masterVolume, settings.masterMuted);
    }

    public void SetMusicMuted(bool muted)
    {
        SettingsSaveData settings = GetSettings();

        if(settings == null)
        {
            return;
        }

        settings.musicMuted = muted;

        ApplyMixerVolume(MusicVolumeParameter, settings.musicVolume, settings.musicMuted);
    }

    public void SetSfxMuted(bool muted)
    {
        SettingsSaveData settings = GetSettings();

        if(settings == null)
        {
            return;
        }

        settings.sfxMuted = muted;

        ApplyMixerVolume(SfxVolumeParameter, settings.sfxVolume, settings.sfxMuted);
    }

    /// <summary>
    /// 设置页面关闭或点击“应用”时调用。
    /// </summary>
    public void SaveSettings()
    {
        if(GameEntry.Save != null)
        {
            GameEntry.Save.Save();
        }
    }

    private SettingsSaveData GetSettings()
    {
        if(GameEntry.Instance == null ||
           GameEntry.Save == null ||
           GameEntry.Save.Data == null)
        {
            return null;
        }

        return GameEntry.Save.Data.settings;
    }

    private void ApplyMixerVolume(string parameterName,float linearVolume,bool muted)
    {
        if(audioMixer == null)
        {
            return;
        }

        float decibels = muted ? MutedDecibels : LinearToDecibels(linearVolume);

        if(!audioMixer.SetFloat(parameterName, decibels))
        {
            Debug.LogWarning(
                $"AudioMixer 中没有找到参数：" +
                $"{parameterName}",
                this
            );
        }
    }

    /// <summary>
    /// 将滑条的 0～1 转换为 AudioMixer 使用的分贝。
    /// </summary>
    private static float LinearToDecibels(
        float linearVolume)
    {
        linearVolume = Mathf.Clamp01(linearVolume);

        if(linearVolume <= 0.0001f)
        {
            return MutedDecibels;
        }

        return Mathf.Log10(linearVolume) * 20f;
    }
}
