using UnityEngine;

public class PlayerSkillInput : MonoBehaviour
{
    public const string PrimarySkillId =
        "ShockWave";

    public const string SecondarySkillId =
        "PiercingBeam";

    public const KeyCode PrimarySkillKey =
        KeyCode.R;

    public const KeyCode SecondarySkillKey =
        KeyCode.F;

    public static string PrimarySkillKeyText =>
        PrimarySkillKey.ToString();

    public static string SecondarySkillKeyText =>
        SecondarySkillKey.ToString();

    private void Update()
    {
        if (Time.timeScale <= 0f)
        {
            return;
        }

        SkillManager skillManager =
            GameEntry.Skill;

        if (skillManager == null)
        {
            return;
        }

        if (Input.GetKeyDown(PrimarySkillKey))
        {
            skillManager.CastSkill(PrimarySkillId);
        }

        if (Input.GetKeyDown(SecondarySkillKey))
        {
            skillManager.CastSkill(SecondarySkillId);
        }
    }
}