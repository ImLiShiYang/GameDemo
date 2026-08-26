using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class AddressableSceneLoader : MonoBehaviour
{
    [Header("Loading UI")]
    [SerializeField] private GameObject loadingRoot;
    [SerializeField] private Slider progressSlider;

    private AsyncOperationHandle<SceneInstance>? loadedSceneHandle;
    private bool isLoading;

    public async void LoadScene(string address)
    {
        try
        {
            await LoadSceneAsync(address);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"场景加载发生异常：{address}\n{exception}"
            );
        }
    }

    public async Task LoadSceneAsync(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            throw new ArgumentException("场景 Address 不能为空。", nameof(address));
        }

        if (isLoading)
        {
            return;
        }

        isLoading = true;
        float loadingShownTime = Time.realtimeSinceStartup;
        SetLoadingVisible(true);
        SetProgress(0f);

        try
        {
            // 等待一帧，让 Unity 先把 Loading UI 渲染出来。
            await Task.Yield();

            await UnloadCurrentAddressableSceneAsync();

            Debug.Log($"开始异步加载场景：{address}");

            AsyncOperationHandle<SceneInstance> handle = Addressables.LoadSceneAsync(
                address,
                LoadSceneMode.Single,
                true
            );

            while (!handle.IsDone)
            {
                SetProgress(handle.PercentComplete);
                await Task.Yield();
            }

            if (handle.Status != AsyncOperationStatus.Succeeded)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }

                throw new Exception($"Addressables 场景加载失败：{address}");
            }

            loadedSceneHandle = handle;
            SetProgress(1f);

            const float minimumVisibleSeconds = 0.6f;

            while (Time.realtimeSinceStartup - loadingShownTime < minimumVisibleSeconds)
            {
                await Task.Yield();
            }

            Debug.Log($"场景加载完成：{address}");
        }
        finally
        {
            isLoading = false;
            SetLoadingVisible(false);
        }
    }

    public async void LoadBuiltInScene(string sceneName)
    {
        try
        {
            await LoadBuiltInSceneAsync(sceneName);
        }
        catch (Exception exception)
        {
            Debug.LogError($"内置场景加载发生异常：{sceneName}\n{exception}");
        }
    }

    public async Task LoadBuiltInSceneAsync(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            throw new ArgumentException("场景名称不能为空。", nameof(sceneName));
        }

        if (isLoading)
        {
            return;
        }

        isLoading = true;
        float loadingShownTime = Time.realtimeSinceStartup;
        SetLoadingVisible(true);
        SetProgress(0f);

        try
        {
            await Task.Yield();

            Debug.Log($"开始异步加载内置场景：{sceneName}");
            AsyncOperation operation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);

            if (operation == null)
            {
                throw new Exception($"无法创建内置场景加载任务：{sceneName}");
            }

            while (!operation.isDone)
            {
                SetProgress(operation.progress * 0.8f);
                await Task.Yield();
            }

            Scene loadedScene = SceneManager.GetSceneByName(sceneName);

            if (!loadedScene.IsValid() || !loadedScene.isLoaded)
            {
                throw new Exception($"内置场景虽然结束加载，但没有找到有效场景：{sceneName}");
            }

            SceneManager.SetActiveScene(loadedScene);
            SetProgress(0.8f);

            // 必须先让 LoginScene 成为已加载场景，再卸载 Addressables 战斗场景。
            // 否则 Unity 会拒绝卸载当前唯一场景。
            await UnloadCurrentAddressableSceneAsync();

            SetProgress(1f);

            const float minimumVisibleSeconds = 0.6f;
            while (Time.realtimeSinceStartup - loadingShownTime < minimumVisibleSeconds)
            {
                await Task.Yield();
            }

            Debug.Log($"内置场景加载完成：{sceneName}");
        }
        finally
        {
            isLoading = false;
            SetLoadingVisible(false);
        }
    }

    private async Task UnloadCurrentAddressableSceneAsync()
    {
        if (!loadedSceneHandle.HasValue)
        {
            return;
        }

        AsyncOperationHandle<SceneInstance> previousHandle = loadedSceneHandle.Value;
        loadedSceneHandle = null;

        if (!previousHandle.IsValid())
        {
            return;
        }

        Debug.Log("开始卸载上一 Addressables 场景。");
        await Addressables.UnloadSceneAsync(previousHandle).Task;
    }

    public async void UnloadLoadedScene()
    {
        if (!loadedSceneHandle.HasValue)
        {
            return;
        }

        AsyncOperationHandle<SceneInstance> handle =
            loadedSceneHandle.Value;

        loadedSceneHandle = null;

        if (!handle.IsValid())
        {
            return;
        }

        SetLoadingVisible(true);

        AsyncOperationHandle<SceneInstance> unloadHandle =
            Addressables.UnloadSceneAsync(handle);

        await unloadHandle.Task;

        SetLoadingVisible(false);
    }

    private void SetLoadingVisible(bool visible)
    {
        if (loadingRoot != null)
        {
            loadingRoot.SetActive(visible);
        }
    }

    private void SetProgress(float value)
    {
        if (progressSlider != null)
        {
            progressSlider.value = Mathf.Clamp01(value);
        }
    }
}
