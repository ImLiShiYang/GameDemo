local MainMenu = {}

function MainMenu.Init()
    print("[Lua] MainMenu module initialized")
end

function MainMenu.OnStartClicked(panel)
    print("[Lua] Main menu start button clicked")
    panel:StartGameFromLua()
end

function MainMenu.OnQuitClicked(panel)
    print("[Lua] Main menu quit button clicked")
    panel:QuitGameFromLua()
end

function MainMenu.Destroy()
    print("[Lua] MainMenu module destroyed")
end

return MainMenu