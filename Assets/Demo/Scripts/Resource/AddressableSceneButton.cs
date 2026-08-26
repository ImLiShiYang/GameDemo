using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 场景内按钮通过运行时查找常驻加载器，避免引用另一个场景中的对象。
/// </summary>
public sealed class AddressableSceneButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private string sceneAddress;

    private void Awake()
    {
        if (button != null)
        {
            button.onClick.AddListener(LoadScene);
        }
    }

    private void OnDestroy()
    {
        if (button != null)
        {
            button.onClick.RemoveListener(LoadScene);
        }
    }

    private void LoadScene()
    {
        AddressableSceneLoader loader = FindFirstObjectByType<AddressableSceneLoader>();

        if (loader == null)
        {
            Debug.LogError("当前场景中没有找到 AddressableSceneLoader。", this);
            return;
        }

        loader.LoadScene(sceneAddress);
    }
}
