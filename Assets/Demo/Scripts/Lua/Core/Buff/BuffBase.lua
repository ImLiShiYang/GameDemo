local BuffBase = {}

function BuffBase.New(config)
    assert(
        type(config) == "table",
        "BuffBase.New 需要传入 table"
    )

    assert(
        type(config.id) == "string" and config.id ~= "",
        "Buff 必须设置 id"
    )

    assert(
        type(config.duration) == "number" and config.duration >= 0,
        "Buff 必须设置合法的 duration"
    )

    assert(
        type(config.maxStack) == "number" and config.maxStack >= 1,
        "Buff 必须设置合法的 maxStack"
    )

    assert(
        type(config.effectType) == "string" and config.effectType ~= "",
        "Buff 必须设置 effectType"
    )

    assert(
        type(config.value) == "number",
        "Buff 必须设置 value"
    )

    return
    {
        id = config.id,
        name = config.name or config.id,
        duration = config.duration,
        maxStack = math.floor(config.maxStack),
        refreshOnAdd = config.refreshOnAdd ~= false,
        effectType = config.effectType,
        value = config.value,
        iconPath = config.iconPath or ""
    }
end

return BuffBase