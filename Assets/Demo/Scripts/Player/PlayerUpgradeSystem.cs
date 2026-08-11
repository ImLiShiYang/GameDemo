using System.Collections.Generic;
using UnityEngine;

public class PlayerUpgradeSystem : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private PlayerCombatStats combatStats;

    [Header("可获得升级")]
    [SerializeField]
    private List<UpgradeData> upgradePool =
        new List<UpgradeData>();

    private readonly Dictionary<UpgradeData, int> upgradeLevels =
        new Dictionary<UpgradeData, int>();

    private void Awake()
    {
        if (combatStats == null)
        {
            combatStats = GetComponent<PlayerCombatStats>();
        }
    }

    public int GetUpgradeLevel(UpgradeData upgrade)
    {
        if (upgrade == null)
        {
            return 0;
        }

        return upgradeLevels.TryGetValue(upgrade, out int level)? level: 0;
    }

    public List<UpgradeData> GetRandomChoices(int choiceCount)
    {
        List<UpgradeData> candidates =
            new List<UpgradeData>();

        foreach (UpgradeData upgrade in upgradePool)
        {
            if (upgrade == null)
            {
                continue;
            }

            int currentLevel =
                GetUpgradeLevel(upgrade);

            if (currentLevel >= upgrade.MaxLevel)
            {
                continue;
            }

            candidates.Add(upgrade);
        }

        // Fisher-Yates 洗牌
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int randomIndex =
                Random.Range(0, i + 1);

            (candidates[i], candidates[randomIndex]) =
                (candidates[randomIndex], candidates[i]);
        }

        if (candidates.Count > choiceCount)
        {
            candidates.RemoveRange(
                choiceCount,
                candidates.Count - choiceCount
            );
        }

        return candidates;
    }

    public bool TryApplyUpgrade(UpgradeData upgrade)
    {
        if (upgrade == null || combatStats == null)
        {
            return false;
        }

        int currentLevel =GetUpgradeLevel(upgrade);            

        if (currentLevel >= upgrade.MaxLevel)
        {
            return false;
        }

        foreach (UpgradeEffect effect in upgrade.Effects)
        {
            combatStats.ApplyEffect(effect);
        }

        upgradeLevels[upgrade] =currentLevel + 1;

        Debug.Log(
            $"获得升级：{upgrade.DisplayName} " +
            $"Lv.{currentLevel + 1}",
            this
        );

        return true;
    }
}