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
        // 这一击已经把玩家打死了，
        // 不再播放普通受击动画，交给死亡动画处理。
        if (health.CurrentHealth <= 0f)
        {
            return;
        }

        playerController.PlayHitReaction();
    }
}