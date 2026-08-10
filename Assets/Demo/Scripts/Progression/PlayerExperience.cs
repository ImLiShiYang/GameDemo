using System;
using UnityEngine;

public class PlayerExperience : MonoBehaviour
{
    [Header("等级")]
    [SerializeField, Min(1)]
    private int level = 1;

    [Header("经验")]
    [SerializeField, Min(1)]
    private int experienceToNextLevel = 100;

    private int currentExperience;

    public int Level => level;

    public int CurrentExperience => currentExperience;

    public int ExperienceToNextLevel => experienceToNextLevel;

    /// <summary>
    /// 当前经验发生变化。
    /// 参数：
    /// 当前经验 / 升级所需经验
    /// </summary>
    public event Action<int, int> ExperienceChanged;

    /// <summary>
    /// 玩家升级。
    /// 参数为升级后的等级。
    /// </summary>
    public event Action<int> LeveledUp;

    public void AddExperience(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        currentExperience += amount;

        Debug.Log(
            $"获得 {amount} 点经验。当前经验：" +
            $"{currentExperience}/{experienceToNextLevel}",
            this
        );

        /*
         * 使用 while：
         * 如果一次获得大量经验，
         * 允许连续升多级。
         */
        while (currentExperience >= experienceToNextLevel)
        {
            currentExperience -= experienceToNextLevel;

            LevelUp();
        }

        ExperienceChanged?.Invoke(currentExperience,experienceToNextLevel);
        
    }

    private void LevelUp()
    {
        level++;

        /*
         * 第一版先简单处理：
         * 每次升级需求经验提高 25%。
         *
         * 后面可以换成配置表。
         */
        experienceToNextLevel =
            Mathf.CeilToInt(experienceToNextLevel * 1.25f);

        Debug.Log(
            $"玩家升级！当前等级：{level}",
            this
        );

        LeveledUp?.Invoke(level);
    }
}