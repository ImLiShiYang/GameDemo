using UnityEngine;

/// <summary>
/// 客户端网络敌人的纯表现生命组件。它不计算伤害，只显示服务器下发的生命值和受击/死亡反馈。
/// </summary>
public sealed class NetworkEntityHealthView : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
    private Renderer[] renderers;
    private NetworkEntity networkEntity;
    private Vector3 baseScale;
    private float damageFlashUntil;
    private bool dead;

    public float CurrentHealth { get; private set; }
    public float MaxHealth { get; private set; }

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    public void Initialize(NetworkEntity entity, float currentHealth, float maxHealth)
    {
        networkEntity = entity;
        renderers = GetComponentsInChildren<Renderer>(true);
        transform.localScale = baseScale;
        dead = false;
        damageFlashUntil = 0f;
        ApplyHealth(currentHealth, maxHealth);
    }

    public void ApplyHealth(float currentHealth, float maxHealth)
    {
        MaxHealth = Mathf.Max(0f, maxHealth);
        CurrentHealth = Mathf.Clamp(currentHealth, 0f, MaxHealth);
        UpdateColor();
    }

    public void PlayDamage(float amount)
    {
        if (dead || amount <= 0f)
        {
            return;
        }

        damageFlashUntil = Time.unscaledTime + 0.12f;
        SetColor(Color.white);
    }

    public void PlayDeath()
    {
        if (dead)
        {
            return;
        }

        dead = true;
        SetColor(new Color(0.15f, 0.15f, 0.15f, 1f));
        transform.localScale = new Vector3(transform.localScale.x, transform.localScale.y * 0.35f, transform.localScale.z);
    }

    private void Update()
    {
        if (!dead && damageFlashUntil > 0f && Time.unscaledTime >= damageFlashUntil)
        {
            damageFlashUntil = 0f;
            UpdateColor();
        }
    }

    private void OnGUI()
    {
        if (dead || networkEntity == null || Camera.main == null)
        {
            return;
        }

        Vector3 screen = Camera.main.WorldToScreenPoint(transform.position + Vector3.up * 1.8f);

        if (screen.z <= 0f)
        {
            return;
        }

        string text = $"E{networkEntity.EntityId}  HP {CurrentHealth:0}/{MaxHealth:0}";
        GUI.Label(new Rect(screen.x - 70f, Screen.height - screen.y, 180f, 24f), text);
    }

    private void UpdateColor()
    {
        float healthRatio = MaxHealth > 0f ? CurrentHealth / MaxHealth : 0f;
        SetColor(Color.Lerp(new Color(0.65f, 0.05f, 0.05f, 1f), new Color(0.9f, 0.3f, 0.1f, 1f), healthRatio));
    }

    private void SetColor(Color color)
    {
        if (renderers == null)
        {
            return;
        }

        foreach (Renderer targetRenderer in renderers)
        {
            targetRenderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetColor(BaseColorId, color);
            propertyBlock.SetColor(ColorId, color);
            targetRenderer.SetPropertyBlock(propertyBlock);
        }
    }
}
