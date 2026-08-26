using UnityEngine;
using UnityEngine.UI;

public class AddressableDemoPanel : MonoBehaviour
{
    private const string Address = "UI/AddressableDemoPanel";

    [SerializeField] private Button closeButton;

    private void Awake()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Close);
        }
    }

    private void OnDestroy()
    {
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Close);
        }
    }

    private void Close()
    {
        if (AddressableResourceManager.Instance == null)
        {
            Destroy(gameObject);
            return;
        }

        AddressableResourceManager.Instance.ReleaseInstance(
            Address,
            gameObject
        );
    }
}