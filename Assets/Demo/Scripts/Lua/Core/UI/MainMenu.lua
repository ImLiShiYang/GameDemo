local MainMenu = {}

local Time = CS.UnityEngine.Time
local Cursor = CS.UnityEngine.Cursor
local CursorLockMode = CS.UnityEngine.CursorLockMode
local Action = CS.System.Action

-- 每个主菜单实例保存独立的生命周期状态与按钮回调。
local panelStates = setmetatable({}, { __mode = "k" })

local function GetOrCreateState(panel)
    local state = panelStates[panel]

    if state == nil then
        state = {
            isOpen = false,
            isStarting = false,
            previousTimeScale = 1,
            previousCursorLockState = CursorLockMode.None,
            previousCursorVisible = true
        }

        panelStates[panel] = state
    end

    return state
end

function MainMenu.Init()
    print("[Lua] MainMenu module initialized")
end

function MainMenu.OnInit(panel)
    local state = GetOrCreateState(panel)

    state.startCallback = Action(function()
        MainMenu.OnStartClicked(panel)
    end)

    state.quitCallback = Action(function()
        MainMenu.OnQuitClicked(panel)
    end)

    panel:BindStartButton(state.startCallback)
    panel:BindQuitButton(state.quitCallback)

    print("[Lua] Main menu view initialized and buttons bound")
end

function MainMenu.OnOpen(panel, args)
    local state = GetOrCreateState(panel)

    state.previousTimeScale = Time.timeScale
    state.previousCursorLockState = Cursor.lockState
    state.previousCursorVisible = Cursor.visible
    state.isOpen = true
    state.isStarting = false
    state.openArgs = args

    Time.timeScale = 0
    Cursor.lockState = CursorLockMode.None
    Cursor.visible = true

    print("[Lua] Main menu opened")
end

function MainMenu.OnRefresh(panel, args)
    local state = GetOrCreateState(panel)
    state.openArgs = args

    Time.timeScale = 0
    Cursor.lockState = CursorLockMode.None
    Cursor.visible = true
end

function MainMenu.OnClose(panel)
    local state = panelStates[panel]

    if state == nil or not state.isOpen then
        return
    end

    Time.timeScale = state.previousTimeScale
    Cursor.lockState = state.previousCursorLockState
    Cursor.visible = state.previousCursorVisible

    state.isOpen = false
    state.openArgs = nil

    print("[Lua] Main menu closed")
end

function MainMenu.OnStartClicked(panel)
    local state = GetOrCreateState(panel)

    if state.isStarting then
        return
    end

    state.isStarting = true

    print("[Lua] Main menu start button clicked")

    -- 流程顺序由 Lua 决定：先关闭菜单，再通知游戏开始。
    panel:CloseFromLua()
    panel:NotifyGameStartedFromLua()
end

function MainMenu.OnQuitClicked(panel)
    print("[Lua] Main menu quit button clicked")
    panel:QuitApplicationFromLua()
end

function MainMenu.OnPanelDestroyed(panel)
    panelStates[panel] = nil
end

function MainMenu.Destroy()
    panelStates = setmetatable({}, { __mode = "k" })
    print("[Lua] MainMenu module destroyed")
end

return MainMenu
