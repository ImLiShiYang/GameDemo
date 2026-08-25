local SkillBase = require("Skill.SkillBase")

local SkillConfig = {}

local skills =
{
    ShockWave = SkillBase.New(
    {
        id = "ShockWave",
        displayName = "冲击波",
        description = "短暂蓄力后，对周围敌人造成范围伤害，并产生较强打断。",
    
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
        description = "向瞄准方向发射射线，贯穿路径上的多个敌人。",
    
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
        config.interruptPower,
        config.description
end

return SkillConfig