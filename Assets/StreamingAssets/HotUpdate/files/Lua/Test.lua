local Event = require("Event")

local function EnemyDropExp()
	print("Drop Exp")
end

local function EnemyUpdateWave()
	print("Update Wave")
end


-- 注册两个函数
Event.On("EnemyDead", EnemyDropExp)
Event.On("EnemyDead", EnemyUpdateWave)


print("===== First Emit =====")

Event.Emit("EnemyDead")


-- 取消第一个函数
--Event.Off("EnemyDead", EnemyDropExp)
Event.Off("EnemyDead", EnemyUpdateWave)

print("===== Second Emit =====")

Event.Emit("EnemyDead")