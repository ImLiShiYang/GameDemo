using UnityEngine;

public class LuaTest : MonoBehaviour
{
    private void Start()
    {
        if (GameEntry.Lua == null)
        {
            Debug.LogError("场景启动时没有找到 LuaManager。", this);
            return;
        }

        GameEntry.Lua.Require("Test");
    }
}