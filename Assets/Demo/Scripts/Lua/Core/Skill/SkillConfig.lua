local SkillConfig = {}

local skills =
{
    ShockWave =
    {
        damage = 30,
        range = 2,
        cooldown = 0.1,
        warningTime = 1,
        interruptPower = 2
    },

    PiercingBeam =
    {
        damage = 60,
        range = 12,
        cooldown = 0.1,
        warningTime = 0,
        interruptPower = 1
    }
}

function SkillConfig.GetSkillValues(skillId)
    local config = skills[skillId]

    if config == nil then
        print(
            "SkillConfig 找不到技能：" ..
            tostring(skillId)
        )

        return nil
    end

    return
        config.damage,
        config.range,
        config.cooldown,
        config.warningTime,
        config.interruptPower
end

return SkillConfig
