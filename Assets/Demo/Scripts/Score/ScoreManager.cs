using UnityEngine;

public sealed class ScoreResult
{
    public int ClearBaseScore;
    public int TimeBonus;
    public int HealthBonus;
    public int FinalScore;
    public int HighestScore;
    public float ClearTime;
    public float RemainingHealthRatio;
    public bool IsNewHighestScore;
}

public class ScoreManager : MonoBehaviour
{
    [Header("Score Rules")]
    [SerializeField, Min(0)] private int clearBaseScore = 5000;
    [SerializeField, Min(0)] private int maximumTimeBonus = 3000;
    [SerializeField, Min(1f)] private float timeBonusDuration = 300f;
    [SerializeField, Min(0)] private int maximumHealthBonus = 2000;

    private float runStartTime;
    private bool runIsActive;

    public int HighestScore
    {
        get
        {
            if (GameEntry.Instance == null || GameEntry.Save == null || GameEntry.Save.Data == null || GameEntry.Save.Data.player == null)
            {
                return 0;
            }

            return Mathf.Max(0, GameEntry.Save.Data.player.highestScore);
        }
    }

    public void BeginRun()
    {
        runStartTime = Time.time;
        runIsActive = true;
    }

    public ScoreResult CompleteVictory(Health playerHealth)
    {
        float clearTime = runIsActive ? Mathf.Max(0f, Time.time - runStartTime) : 0f;
        runIsActive = false;

        float timeRatio = 1f - Mathf.Clamp01(clearTime / Mathf.Max(1f, timeBonusDuration));
        int timeBonus = Mathf.RoundToInt(maximumTimeBonus * timeRatio);

        float remainingHealthRatio = GetRemainingHealthRatio(playerHealth);
        int healthBonus = Mathf.RoundToInt(maximumHealthBonus * remainingHealthRatio);
        int finalScore = clearBaseScore + timeBonus + healthBonus;

        int previousHighestScore = HighestScore;
        bool isNewHighestScore = finalScore > previousHighestScore;

        if (isNewHighestScore && GameEntry.Save != null && GameEntry.Save.Data != null && GameEntry.Save.Data.player != null)
        {
            GameEntry.Save.Data.player.highestScore = finalScore;
            GameEntry.Save.Save();
        }

        return new ScoreResult
        {
            ClearBaseScore = clearBaseScore,
            TimeBonus = timeBonus,
            HealthBonus = healthBonus,
            FinalScore = finalScore,
            HighestScore = isNewHighestScore ? finalScore : previousHighestScore,
            ClearTime = clearTime,
            RemainingHealthRatio = remainingHealthRatio,
            IsNewHighestScore = isNewHighestScore
        };
    }

    private static float GetRemainingHealthRatio(Health playerHealth)
    {
        if (playerHealth == null || playerHealth.MaxHealth <= 0f)
        {
            return 0f;
        }

        return Mathf.Clamp01(playerHealth.CurrentHealth / playerHealth.MaxHealth);
    }
}
