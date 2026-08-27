local BuffBase = require("Buff.BuffBase")

local BuffConfig = {}

local buffs =
{
    EnergyShield = BuffBase.New(
    {
        id = "EnergyShield",
        name = "能量护盾",

        duration = 8,
        maxStack = 3,
        refreshOnAdd = true,

        effectType = "Shield",
        value = 25,

        iconPath = "UI/Buffs/EnergyShield"
    }),
    
    SpeedBoost = BuffBase.New(
    {
        id = "SpeedBoost",
        name = "加速",
    
        duration = 6,
        maxStack = 3,
        refreshOnAdd = true,
    
        effectType = "MoveSpeed",
        value = 0.2,
    
        iconPath = "UI/Buffs/SpeedBoost"
    })
}

function BuffConfig.GetBuffValues(buffId)
    local config = buffs[buffId]

    if config == nil then
        print(
            "BuffConfig 找不到 Buff：" ..
            tostring(buffId)
        )

        return nil
    end

    return
        config.duration,
        config.maxStack,
        config.refreshOnAdd,
        config.effectType,
        config.value,
        config.iconPath
end

return BuffConfig