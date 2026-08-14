local SkillBase = {}

function SkillBase.New(config)
    assert(
        type(config) == "table",
        "SkillBase.New 需要传入 table"
    )

    assert(
        type(config.id) == "string" and config.id ~= "",
        "技能必须配置 id"
    )

    assert(
        type(config.executor) == "string" and config.executor ~= "",
        "技能必须配置 executor"
    )

    -- 公共字段默认值
    config.displayName =
        config.displayName or config.id

    config.damage =
        math.max(0, tonumber(config.damage) or 0)

    config.range =
        math.max(0, tonumber(config.range) or 0)

    config.cooldown =
        math.max(0, tonumber(config.cooldown) or 0)

    config.warningTime =
        math.max(0, tonumber(config.warningTime) or 0)

    config.interruptPower =
        math.max(0, tonumber(config.interruptPower) or 0)

    return config
end

return SkillBase