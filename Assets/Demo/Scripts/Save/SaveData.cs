using System;


[Serializable]
public class SaveData
{
    public int version = 1;

    public PlayerSaveData player = new PlayerSaveData();

    public SettingsSaveData settings = new SettingsSaveData();
}


[Serializable]
public class PlayerSaveData
{
    public int highestScore = 0;

    public bool tutorialShown = false;
}


[Serializable]
public class SettingsSaveData
{
    public float masterVolume = 1f;

    public float musicVolume = 1f;

    public float sfxVolume = 1f;


    public bool masterMuted = false;

    public bool musicMuted = false;

    public bool sfxMuted = false;


    public int qualityLevel = 2;
}