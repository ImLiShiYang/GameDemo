using UnityEngine;

[RequireComponent(typeof(Health))]
public sealed class PlayerShieldVisual : MonoBehaviour
{
    [Header("Persistent Shield")]
    [Tooltip("护盾存在期间保持激活的角色子物体或特效根节点。")]
    [SerializeField]
    private GameObject shieldEffectRoot;

    [Header("Optional One-shot Effects")]
    [Tooltip("每次从无护盾变为有护盾时播放。")]
    [SerializeField]
    private ParticleSystem shieldGainedEffect;

    [Tooltip("护盾被伤害打空时播放。不要放在 Shield Effect Root 下面。")]
    [SerializeField]
    private ParticleSystem shieldBrokenEffect;

    private Health health;
    private bool shieldActive;

    private void Awake()
    {
        health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        if (health == null)
        {
            health = GetComponent<Health>();
        }

        health.ShieldChanged += HandleShieldChanged;
        health.ShieldDepleted += HandleShieldDepleted;

        SetShieldActive(
            health.CurrentShield > 0f,
            false
        );
    }

    private void OnDisable()
    {
        if (health == null)
        {
            return;
        }

        health.ShieldChanged -= HandleShieldChanged;
        health.ShieldDepleted -= HandleShieldDepleted;
    }

    private void HandleShieldChanged(
        float current,
        float capacity)
    {
        SetShieldActive(current > 0f, true);
    }

    private void HandleShieldDepleted()
    {
        if (shieldBrokenEffect != null)
        {
            shieldBrokenEffect.Play(true);
        }
    }

    private void SetShieldActive(
        bool active,
        bool playGainedEffect)
    {
        bool becameActive = active && !shieldActive;
        shieldActive = active;

        if (shieldEffectRoot != null &&
            shieldEffectRoot.activeSelf != active)
        {
            shieldEffectRoot.SetActive(active);
        }

        if (becameActive &&
            playGainedEffect &&
            shieldGainedEffect != null)
        {
            shieldGainedEffect.Play(true);
        }
    }
}
