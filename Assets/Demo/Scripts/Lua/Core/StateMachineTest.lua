local StateMachine = require("StateMachine")

local fsm = StateMachine.New()

-- 模拟玩家和敌人的距离
local distance = 20


-- =========================
-- Idle
-- =========================

local idleState = {}

function idleState.Enter()
	print("Enter Idle")
end

function idleState.Update()
	print("Idle Update, distance =", distance)

	-- 玩家靠近了
	if distance < 10 then
		fsm.ChangeState("Chase")
	end
end

function idleState.Exit()
	print("Exit Idle")
end


-- =========================
-- Chase
-- =========================

local chaseState = {}

function chaseState.Enter()
	print("Enter Chase")
end

function chaseState.Update()
	print("Chase Update, distance =", distance)

	-- 已经追到攻击距离
	if distance <= 2 then
		fsm.ChangeState("Attack")

	-- 玩家又跑远了
	elseif distance >= 10 then
		fsm.ChangeState("Idle")
	end
end

function chaseState.Exit()
	print("Exit Chase")
end


-- =========================
-- Attack
-- =========================

local attackState = {}

function attackState.Enter()
	print("Enter Attack")
end

function attackState.Update()
	print("Attack Player")

	-- 玩家离开攻击距离
	if distance > 2 then
		fsm.ChangeState("Chase")
	end
end

function attackState.Exit()
	print("Exit Attack")
end


-- 注册状态
fsm.AddState("Idle", idleState)
fsm.AddState("Chase", chaseState)
fsm.AddState("Attack", attackState)


-- 一开始进入 Idle
fsm.ChangeState("Idle")


print("===== Player Far =====")

distance = 20
fsm.Update()


print("===== Player Coming =====")

distance = 7
fsm.Update()


print("===== Chase =====")

fsm.Update()


print("===== Player Very Close =====")

distance = 1
fsm.Update()


print("===== Attack =====")

fsm.Update()