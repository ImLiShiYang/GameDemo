using UnityEngine;

[RequireComponent(typeof(Health))]
[RequireComponent(typeof(GrayboxPlayerController))]
public class PlayerHitReaction : MonoBehaviour
{
    private Health health;
    private GrayboxPlayerController playerController;

    private void Awake()
    {
        health = GetComponent<Health>();
        playerController = GetComponent<GrayboxPlayerController>();
    }

    private void OnEnable()
    {
        health.Damaged += OnDamaged;
    }

    private void OnDisable()
    {
        health.Damaged -= OnDamaged;
    }

    private void OnDamaged(DamageInfo damageInfo)
    {
        PlayerHitKind kind = health.CurrentHealth <= 0f ? PlayerHitKind.Lethal :
            damageInfo.InterruptPower > 0 ? PlayerHitKind.Heavy : PlayerHitKind.Normal;
        playerController.PlayHitReaction(damageInfo.HitDirection, kind);
    }
}
