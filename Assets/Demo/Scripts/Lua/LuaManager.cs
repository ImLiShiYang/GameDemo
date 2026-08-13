using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using XLua;

/// <summary>
/// 项目唯一的 Lua 环境入口。
/// 负责 LuaEnv 生命周期、模块加载、模块更新、C# 调 Lua 和异常日志。
/// </summary>
public sealed class LuaManager : MonoBehaviour
{
    private sealed class LuaModule : IDisposable
    {
        public LuaTable Table;
        public LuaFunction Update;
        public LuaFunction Destroy;

        public void Dispose()
        {
            Update?.Dispose();
            Update = null;

            Destroy?.Dispose();
            Destroy = null;

            Table?.Dispose();
            Table = null;
        }
    }

    private const string LuaRootRelativePath ="Demo/Scripts/Lua/Core";
        
    public bool IsInitialized => luaEnv != null;

    private readonly Dictionary<string, LuaModule> modules =new Dictionary<string, LuaModule>();
        

    private LuaEnv luaEnv;
    private void Awake()
    {
        Initialize();
    }

    private void Update()
    {
        if (luaEnv == null)
        {
            return;
        }

        foreach (KeyValuePair<string, LuaModule> pair in modules)
        {
            LuaFunction updateFunction = pair.Value.Update;

            if (updateFunction == null)
            {
                continue;
            }

            try
            {
                updateFunction.Action(
                    Time.deltaTime,
                    Time.unscaledDeltaTime
                );
            }
            catch (Exception exception)
            {
                LogLuaException(
                    $"Lua 模块 Update 失败：{pair.Key}",
                    exception
                );
            }
        }

        try
        {
            luaEnv.Tick();
        }
        catch (Exception exception)
        {
            LogLuaException("LuaEnv.Tick 失败", exception);
        }
    }

    /// <summary>
    /// 加载普通 Lua 文件。该文件可以不返回 table。
    /// </summary>
    public bool Require(string moduleName)
    {
        if (!ValidateModuleName(moduleName) || !Initialize())
        {
            return false;
        }

        try
        {
            luaEnv.DoString(
                $"require('{EscapeLuaString(moduleName)}')",
                $"Require:{moduleName}"
            );

            return true;
        }
        catch (Exception exception)
        {
            LogLuaException(
                $"加载 Lua 模块失败：{moduleName}",
                exception
            );

            return false;
        }
    }

    /// <summary>
    /// 加载返回 table 的业务模块，并缓存它的生命周期函数。
    /// </summary>
    public bool LoadModule(string moduleName)
    {
        if (!ValidateModuleName(moduleName) || !Initialize())
        {
            return false;
        }

        if (modules.ContainsKey(moduleName))
        {
            return true;
        }

        LuaModule module = null;

        try
        {
            object[] results = luaEnv.DoString(
                $"return require('{EscapeLuaString(moduleName)}')",
                $"LoadModule:{moduleName}"
            );

            LuaTable table =
                results != null && results.Length > 0
                    ? results[0] as LuaTable
                    : null;

            if (table == null)
            {
                Debug.LogError(
                    $"Lua 业务模块必须 return table：{moduleName}",
                    this
                );

                return false;
            }

            module = new LuaModule
            {
                Table = table,
                Update = table.Get<string, LuaFunction>("Update"),
                Destroy = table.Get<string, LuaFunction>("Destroy")
            };

            LuaFunction initFunction =
                table.Get<string, LuaFunction>("Init");

            try
            {
                initFunction?.Call();
            }
            finally
            {
                initFunction?.Dispose();
            }

            modules.Add(moduleName, module);
            return true;
        }
        catch (Exception exception)
        {
            module?.Dispose();

            LogLuaException(
                $"初始化 Lua 业务模块失败：{moduleName}",
                exception
            );

            return false;
        }
    }
    
    /// <summary>
    /// 调用 Lua 业务模块中的指定函数，并获取返回值。
    /// </summary>
    public object[] CallWithResults(string moduleName,string functionName,params object[] args)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            Debug.LogError(
                "Lua 函数名不能为空。",
                this
            );

            return null;
        }

        if (!LoadModule(moduleName))
        {
            return null;
        }

        LuaModule module = modules[moduleName];
        LuaFunction function = null;

        try
        {
            function =module.Table.Get<string, LuaFunction>(functionName);

            if (function == null)
            {
                Debug.LogError(
                    $"Lua 模块 {moduleName} 中没有函数 {functionName}。",
                    this
                );

                return null;
            }

            return function.Call(args);
        }
        catch (Exception exception)
        {
            LogLuaException(
                $"调用 Lua 函数失败：{moduleName}.{functionName}",
                exception
            );

            return null;
        }
        finally
        {
            function?.Dispose();
        }
    }

    /// <summary>
    /// 调用业务模块中的指定函数。
    /// </summary>
    public bool Call(string moduleName,string functionName,params object[] args)
    {
        if (string.IsNullOrWhiteSpace(functionName))
        {
            Debug.LogError("Lua 函数名不能为空。", this);
            return false;
        }

        if (!LoadModule(moduleName))
        {
            return false;
        }

        LuaModule module = modules[moduleName];
        LuaFunction function = null;

        try
        {
            function =
                module.Table.Get<string, LuaFunction>(functionName);

            if (function == null)
            {
                Debug.LogError(
                    $"Lua 模块 {moduleName} 中没有函数 {functionName}。",
                    this
                );

                return false;
            }

            function.Call(args);
            return true;
        }
        catch (Exception exception)
        {
            LogLuaException(
                $"调用 Lua 函数失败：{moduleName}.{functionName}",
                exception
            );

            return false;
        }
        finally
        {
            function?.Dispose();
        }
    }

    public bool UnloadModule(string moduleName)
    {
        if (!modules.TryGetValue(moduleName, out LuaModule module))
        {
            return false;
        }

        modules.Remove(moduleName);

        try
        {
            module.Destroy?.Call();
        }
        catch (Exception exception)
        {
            LogLuaException(
                $"销毁 Lua 模块失败：{moduleName}",
                exception
            );
        }
        finally
        {
            module.Dispose();
        }

        return true;
    }

    private bool Initialize()
    {
        if (luaEnv != null)
        {
            return true;
        }

        try
        {
            luaEnv = new LuaEnv();
            luaEnv.AddLoader(CustomLoader);

            Debug.Log("LuaManager 初始化完成。", this);
            return true;
        }
        catch (Exception exception)
        {
            LogLuaException("LuaManager 初始化失败", exception);

            luaEnv?.Dispose();
            luaEnv = null;
            return false;
        }
    }

    private byte[] CustomLoader(ref string moduleName)
    {
        string relativeModulePath =
            moduleName.Replace('.', Path.DirectorySeparatorChar);

        string fullPath = Path.Combine(
            Application.dataPath,
            LuaRootRelativePath,
            relativeModulePath + ".lua"
        );

        if (!File.Exists(fullPath))
        {
            return null;
        }

        moduleName = fullPath;

        byte[] bytes = File.ReadAllBytes(fullPath);
        return RemoveUtf8Bom(bytes);
    }

    private bool ValidateModuleName(string moduleName)
    {
        if (!string.IsNullOrWhiteSpace(moduleName))
        {
            return true;
        }

        Debug.LogError("Lua 模块名不能为空。", this);
        return false;
    }

    private static string EscapeLuaString(string value)
    {
        return value.Replace("\\", "\\\\")
            .Replace("'", "\\'");
    }

    private static byte[] RemoveUtf8Bom(byte[] bytes)
    {
        if (bytes == null ||
            bytes.Length < 3 ||
            bytes[0] != 0xEF ||
            bytes[1] != 0xBB ||
            bytes[2] != 0xBF)
        {
            return bytes;
        }

        byte[] withoutBom =
            new byte[bytes.Length - 3];

        Buffer.BlockCopy(
            bytes,
            3,
            withoutBom,
            0,
            withoutBom.Length
        );

        return withoutBom;
    }

    private void LogLuaException(
        string context,
        Exception exception)
    {
        Debug.LogError(
            $"{context}\n{exception}",
            this
        );
    }

    private void OnDestroy()
    {

        string[] moduleNames =
            new string[modules.Count];

        modules.Keys.CopyTo(moduleNames, 0);

        foreach (string moduleName in moduleNames)
        {
            UnloadModule(moduleName);
        }

        if (luaEnv == null)
        {
            return;
        }

        try
        {
            luaEnv.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            luaEnv = null;
        }
    }
}