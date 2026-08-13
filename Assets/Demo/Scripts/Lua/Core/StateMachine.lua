local StateMachine = {}

-- 创建一个新的状态机
function StateMachine.New()
	local machine = {}

	machine.states = {}
	machine.currentState = nil

	-- 添加状态
	function machine.AddState(name, state)
		machine.states[name] = state
	end

	function machine.ChangeState(name)

		local newState = machine.states[name]

		if newState == nil then
			return
		end

		-- 如果之前有状态
		-- 先退出之前的状态
		if machine.currentState ~= nil then
			machine.currentState.Exit()
		end

		-- 设置新的当前状态
		machine.currentState = newState

		-- 进入新状态
		machine.currentState.Enter()
	end

	-- 更新当前状态
	function machine.Update()

	-- 如果当前没有状态，就什么都不做
		if machine.currentState == nil then
			return
		end

		-- 执行当前状态自己的 Update
		machine.currentState.Update()
	end
	
	return machine
end


return StateMachine