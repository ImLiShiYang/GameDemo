local SkillBase = require("Skill.SkillBase")

local SkillConfig = {}

local skills =
{
    ShockWave = SkillBase.New(
    {
        id = "ShockWave",
        displayName = "冲击波",

        -- 告诉 C# 应该调用哪种技能执行逻辑
        executor = "ShockWave",

        damage = 30,
        range = 2,
        cooldown = 5,
        warningTime = 1,
        interruptPower = 2
    }),

    PiercingBeam = SkillBase.New(
    {
        id = "PiercingBeam",
        displayName = "穿透射线",

        executor = "PiercingBeam",

        damage = 60,
        range = 12,
        cooldown = 6,
        warningTime = 0,
        interruptPower = 1
    })
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
        config.id,
        config.displayName,
        config.executor,
        config.damage,
        config.range,
        config.cooldown,
        config.warningTime,
        config.interruptPower
end

return SkillConfig