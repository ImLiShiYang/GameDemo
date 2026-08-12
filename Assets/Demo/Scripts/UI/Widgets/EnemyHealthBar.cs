using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Health))]
public class EnemyHealthBar : MonoBehaviour
{
    [Header("References")]

    [SerializeField]
    private GameObject healthBarRoot;

    [SerializeField]
    private Slider healthSlider;

    [Header("Display")]

    [Tooltip("受击后血条持续显示多久。")]
    [SerializeField, Min(0f)]
    private float visibleDuration = 3f;

    [Tooltip("血条是否始终朝向主摄像机。")]
    [SerializeField]
    private bool faceCamera = true;

    private Health health;
    private Camera mainCamera;

    private float hideTime;

    private bool isVisible;

    private void Awake()
    {
        health = GetComponent<Health>();

        mainCamera = Camera.main;

        RefreshHealth();

        HideImmediate();
    }

    private void OnEnable()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }

        if (health != null)
        {
            health.Damaged += HandleDamaged;
            health.Died += HandleDied;
        }

        RefreshHealth();

        HideImmediate();
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Damaged -= HandleDamaged;
            health.Died -= HandleDied;
        }
    }

    private void Update()
    {
        if (!isVisible)
        {
            return;
        }

        if (Time.time >= hideTime)
        {
            HideImmediate();
        }
    }

    private void LateUpdate()
    {
        if (!faceCamera ||
            healthBarRoot == null)
        {
            return;
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            return;
        }

        healthBarRoot.transform.rotation =mainCamera.transform.rotation;
    }

    private void HandleDamaged(DamageInfo damageInfo)
    {
        RefreshHealth();

        Show();
    }

    private void HandleDied()
    {
        RefreshHealth();

        HideImmediate();
    }

    private void RefreshHealth()
    {
        if (health == null ||
            healthSlider == null)
        {
            return;
        }

        float maxHealth =
            health.MaxHealth;

        float currentHealth =
            health.CurrentHealth;

        healthSlider.value =
            maxHealth > 0f
                ? currentHealth / maxHealth
                : 0f;
    }

    private void Show()
    {
        if (healthBarRoot == null)
        {
            return;
        }

        healthBarRoot.SetActive(true);

        isVisible = true;

        hideTime =
            Time.time + visibleDuration;
    }

    private void HideImmediate()
    {
        isVisible = false;

        if (healthBarRoot != null)
        {
            healthBarRoot.SetActive(false);
        }
    }
}