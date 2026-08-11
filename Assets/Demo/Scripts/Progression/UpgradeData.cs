using System;
using System.Collections.Generic;
using UnityEngine;

public enum UpgradeStat
{
    // 射速倍率
    FireIntervalMultiplier,
    // 穿透次数
    PierceCount,
    // 一次射出的子弹数量
    ProjectileCount,
    // 扩散角度
    SpreadAngle
}

public enum UpgradeOperation
{
    Add,
    Multiply
}

[Serializable]
public class UpgradeEffect
{
    public UpgradeStat stat;

    public UpgradeOperation operation = UpgradeOperation.Add;

    public float value;
}

[CreateAssetMenu(
    fileName = "Upgrade_",
    menuName = "Game/Upgrade Data")]
public class UpgradeData : ScriptableObject
{
    [Header("显示")]
    [SerializeField]
    private string displayName;

    [SerializeField, TextArea(2, 4)]
    private string description;

    [SerializeField]
    private Sprite icon;

    [Header("等级")]
    [SerializeField, Min(1)]
    private int maxLevel = 1;

    [Header("效果")]
    [SerializeField]
    private List<UpgradeEffect> effects =
        new List<UpgradeEffect>();

    public string DisplayName => displayName;

    public string Description => description;

    public Sprite Icon => icon;

    public int MaxLevel => maxLevel;

    public IReadOnlyList<UpgradeEffect> Effects => effects;
}