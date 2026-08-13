local Event = {}

local listeners = {}

-- 注册函数
function Event.On(eventName, callback)
	if listeners[eventName] == nil then
		listeners[eventName] = {}
	end

	table.insert(listeners[eventName], callback)
end

-- 取消注册
function Event.Off(eventName, callback)
	local callbacks = listeners[eventName]

	-- 这个事件根本没人注册
	if callbacks == nil then
		return
	end

	-- 找到要删除的那个函数
	for i = #callbacks, 1, -1 do
		if callbacks[i] == callback then
			table.remove(callbacks, i)
			return
		end
	end
end

--执行函数
function Event.Emit(eventName, ...)
	local callbacks = listeners[eventName]

	if callbacks == nil then
		return
	end
	
	--执行绑定的所有函数
	for i = 1, #callbacks do
		callbacks[i](...)
	end
end

return Event