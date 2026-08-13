using UnityEngine;

public class PlayerSkillInput : MonoBehaviour
{
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            CastSkill1();
        }

        if (Input.GetKeyDown(KeyCode.F))
        {
            CastSkill2();
        }
    }

    private void CastSkill1()
    {
        GameEntry.Skill.CastSkill("ShockWave");
    }

    private void CastSkill2()
    {
        GameEntry.Skill.CastSkill("PiercingBeam");
    }
}