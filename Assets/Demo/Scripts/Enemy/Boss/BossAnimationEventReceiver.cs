using UnityEngine;

public class BossAnimationEventReceiver : MonoBehaviour
{
    private BossController bossController;

    private void Awake()
    {
        bossController = GetComponentInParent<BossController>();
    }

    public void AnimationEvent_AttackHit()
    {
        if (bossController != null)
        {
            bossController.AnimationEvent_AttackHit();
        }
    }
}