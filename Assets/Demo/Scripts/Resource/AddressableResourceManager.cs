using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// Addressables 资源管理器。
/// 负责：
/// 1. 按 Address 异步加载 Prefab。
/// 2. 缓存已经加载过的资源 Handle，避免重复加载。
/// 3. 根据已经加载的 Prefab 创建 GameObject 实例。
/// 4. 通过引用计数记录某个资源当前还有多少个实例在使用。
/// 5. 当最后一个实例被释放时，再释放对应的 Addressables 资源。
/// </summary>
public class AddressableResourceManager : MonoBehaviour
{
    /// <summary>
    /// 单例，方便其他脚本通过 AddressableResourceManager.Instance 访问资源管理器。
    /// </summary>
    public static AddressableResourceManager Instance { get; private set; }

    /// <summary>
    /// 资源缓存表。
    /// Key：Address，例如 "UI/AddressableDemoPanel"。
    /// Value：这个资源对应的异步加载 Handle。
    ///
    /// Handle 可以理解成 Addressables 加载资源后返回的一张“资源凭证”。
    /// 通过它可以：
    /// 1. 等待资源加载完成。
    /// 2. 判断加载是否成功。
    /// 3. 取得加载完成后的资源。
    /// 4. 最后释放资源。
    /// </summary>
    private readonly Dictionary<string, AsyncOperationHandle<GameObject>> prefabHandles = new();

    /// <summary>
    /// 资源引用计数表。
    /// Key：Address。
    /// Value：这个资源当前创建了多少个仍在使用的实例。
    ///
    /// 例如：
    /// "UI/AddressableDemoPanel" -> 3
    /// 表示同一个 Panel Prefab 当前有 3 个实例正在使用。
    /// </summary>
    private readonly Dictionary<string, int> referenceCounts = new();

    /// <summary>
    /// 音频资源缓存表。音频不需要实例化，加载完成后直接交给 AudioSource 播放。
    /// </summary>
    private readonly Dictionary<string, AsyncOperationHandle<AudioClip>> audioHandles = new();

    /// <summary>
    /// 音频资源引用计数。每次 LoadAudioAsync 成功后 +1，每次 ReleaseAudio 时 -1。
    /// </summary>
    private readonly Dictionary<string, int> audioReferenceCounts = new();

    private void Awake()
    {
        // 如果已经存在另一个资源管理器，就销毁当前这个，保证全局只有一个实例。
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        // 保存单例引用。
        Instance = this;

        // 切换场景时不销毁资源管理器，让它可以跨场景继续管理资源。
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// 根据 Address 异步加载一个 GameObject 类型的 Prefab，
    /// 然后在场景中实例化，并返回创建出来的 GameObject。
    /// </summary>
    /// <param name="address">Addressables 中配置的资源地址。</param>
    /// <param name="parent">实例化后要挂到哪个父节点下面，可以为空。</param>
    /// <returns>最终创建出来的 GameObject 实例。</returns>
    public async Task<GameObject> InstantiateAsync(string address, Transform parent = null)
    {
        // Address 不能为空，否则 Addressables 不知道要加载哪个资源。
        if (string.IsNullOrEmpty(address))
        {
            throw new ArgumentException("Addressables 地址不能为空。", nameof(address));
        }

        // 先检查这个 Address 对应的资源是否已经加载过。
        // 如果 prefabHandles 中找不到，说明这是第一次加载。
        if (!prefabHandles.TryGetValue(address, out AsyncOperationHandle<GameObject> handle))
        {
            Debug.Log($"首次加载资源：{address}");

            // 根据 Address 异步加载一个 GameObject 类型的资源。
            // 注意：这里只是“加载 Prefab 资源到内存”，还没有在场景中生成实例。
            handle = Addressables.LoadAssetAsync<GameObject>(address);

            // 把 Handle 保存到缓存表。
            // 下次再请求相同 Address 时，就不需要重新 LoadAssetAsync。
            prefabHandles[address] = handle;
        }
        else
        {
            // 已经有 Handle，说明这个资源之前加载过，直接复用缓存。
            Debug.Log($"使用缓存资源：{address}");
        }

        // 等待这次异步资源加载完成。
        // await 不会让整个游戏卡死，而是暂停当前异步函数，加载完成后再继续执行。
        await handle.Task;

        // 加载任务结束后，检查是否真的加载成功。
        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            // 加载失败，这个 Handle 不能继续留在缓存中。
            prefabHandles.Remove(address);

            // Handle 仍然有效时，将它释放掉，避免残留资源引用。
            if (handle.IsValid())
            {
                Addressables.Release(handle);
            }

            // 抛出异常，让调用这个函数的代码知道加载失败了。
            throw new Exception($"Addressables 资源加载失败：{address}");
        }

        // 取得这个 Address 当前的实例数量。
        // 如果字典里还没有这个 Address，TryGetValue 会让 currentCount 保持为 0。
        referenceCounts.TryGetValue(address, out int currentCount);

        // 当前要新创建一个实例，所以引用计数 +1。
        referenceCounts[address] = currentCount + 1;

        // handle.Result 是 Addressables 加载完成后的 Prefab Asset。
        // Instantiate 才是真正根据这个 Prefab 在场景中创建 GameObject 实例。
        GameObject instance = Instantiate(handle.Result, parent, false);

        // Unity 实例化 Prefab 时默认会在名字后面加 "(Clone)"。
        // 这里把实例名字恢复成原 Prefab 的名字。
        instance.name = handle.Result.name;

        Debug.Log($"资源实例化成功：{address}，当前实例数：{referenceCounts[address]}");

        // 把创建出来的 GameObject 返回给调用者。
        return instance;
    }

    /// <summary>
    /// 释放一个由 InstantiateAsync 创建出来的资源实例。
    /// 先销毁场景里的 GameObject，
    /// 再减少引用计数。
    /// 当引用计数降到 0 时，才真正 Release 对应的 Addressables 资源。
    /// </summary>
    /// <param name="address">这个实例对应的 Address。</param>
    /// <param name="instance">要销毁的 GameObject 实例。</param>
    public void ReleaseInstance(string address, GameObject instance)
    {
        // 先销毁场景中的 GameObject 实例。
        // 注意：Destroy 只销毁 Instance，不代表 Addressables 中加载的 Prefab Asset 已经释放。
        if (instance != null)
        {
            Destroy(instance);
        }

        // 查询这个 Address 当前的引用计数。
        // 如果没有记录，说明这个实例并不是正常通过当前管理器登记出来的。
        if (!referenceCounts.TryGetValue(address, out int currentCount))
        {
            Debug.LogWarning($"释放了一个未记录的资源实例：{address}");
            return;
        }

        // 当前有一个实例不再使用，所以引用计数 -1。
        currentCount--;

        // 如果引用计数仍然大于 0，
        // 说明还有其他实例正在使用同一个 Prefab Asset，不能释放资源。
        if (currentCount > 0)
        {
            referenceCounts[address] = currentCount;
            Debug.Log($"资源仍被使用：{address}，剩余实例数：{currentCount}");
            return;
        }

        // 引用计数已经变成 0，
        // 说明没有任何实例继续使用这个资源，可以移除引用计数记录。
        referenceCounts.Remove(address);

        // 找到这个 Address 当初加载资源时保存的 Handle。
        if (!prefabHandles.TryGetValue(address, out AsyncOperationHandle<GameObject> handle))
        {
            return;
        }

        // Handle 有效时，通知 Addressables：
        // 当前代码已经不再需要这个资源，可以释放对应的资源引用。
        if (handle.IsValid())
        {
            Addressables.Release(handle);
        }

        // Handle 已经释放，不应该继续留在缓存字典中。
        prefabHandles.Remove(address);

        Debug.Log($"资源已经完全释放：{address}");
    }

    /// <summary>
    /// 判断某个 Address 当前是否已经被这个资源管理器加载并缓存。
    /// </summary>
    public bool IsLoaded(string address)
    {
        return prefabHandles.ContainsKey(address);
    }

    /// <summary>
    /// 按 Address 异步加载并缓存 AudioClip。
    /// 同一个地址已经在加载或已经加载完成时，会复用同一个 Handle。
    /// </summary>
    public async Task<AudioClip> LoadAudioAsync(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            throw new ArgumentException("音频 Addressables 地址不能为空。", nameof(address));
        }

        if (!audioHandles.TryGetValue(address, out AsyncOperationHandle<AudioClip> handle))
        {
            Debug.Log($"首次加载音频：{address}");
            handle = Addressables.LoadAssetAsync<AudioClip>(address);
            audioHandles[address] = handle;
        }
        else
        {
            Debug.Log($"使用缓存音频：{address}");
        }

        await handle.Task;

        if (handle.Status != AsyncOperationStatus.Succeeded)
        {
            if (audioHandles.TryGetValue(address, out AsyncOperationHandle<AudioClip> cachedHandle) &&
                cachedHandle.Equals(handle))
            {
                audioHandles.Remove(address);

                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }
            }

            throw new Exception($"Addressables 音频加载失败：{address}");
        }

        audioReferenceCounts.TryGetValue(address, out int currentCount);
        audioReferenceCounts[address] = currentCount + 1;

        Debug.Log($"音频加载成功：{address}，当前引用数：{audioReferenceCounts[address]}");
        return handle.Result;
    }

    /// <summary>
    /// 释放一次 AudioClip 使用权。最后一个使用者释放后才会释放 Addressables Handle。
    /// 调用前应先让 AudioSource 停止播放并将 clip 设为 null。
    /// </summary>
    public void ReleaseAudio(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return;
        }

        if (!audioReferenceCounts.TryGetValue(address, out int currentCount))
        {
            Debug.LogWarning($"释放了一个未记录的音频资源：{address}");
            return;
        }

        currentCount--;

        if (currentCount > 0)
        {
            audioReferenceCounts[address] = currentCount;
            Debug.Log($"音频仍被使用：{address}，剩余引用数：{currentCount}");
            return;
        }

        audioReferenceCounts.Remove(address);

        if (!audioHandles.TryGetValue(address, out AsyncOperationHandle<AudioClip> handle))
        {
            return;
        }

        if (handle.IsValid())
        {
            Addressables.Release(handle);
        }

        audioHandles.Remove(address);
        Debug.Log($"音频已经完全释放：{address}");
    }

    public bool IsAudioLoaded(string address)
    {
        return audioHandles.ContainsKey(address);
    }

    private void OnDestroy()
    {
        // 如果被销毁的不是当前单例，就不做后面的全局资源清理。
        if (Instance != this)
        {
            return;
        }

        // 资源管理器本身被销毁时，
        // 把它当前仍然持有的所有 Addressables Handle 全部释放掉。
        foreach (AsyncOperationHandle<GameObject> handle in prefabHandles.Values)
        {
            if (handle.IsValid())
            {
                Addressables.Release(handle);
            }
        }

        foreach (AsyncOperationHandle<AudioClip> handle in audioHandles.Values)
        {
            if (handle.IsValid())
            {
                Addressables.Release(handle);
            }
        }

        // 清空所有本地缓存记录。
        prefabHandles.Clear();
        referenceCounts.Clear();
        audioHandles.Clear();
        audioReferenceCounts.Clear();

        // 清除单例引用。
        Instance = null;
    }
}
