using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerRootMotionRelay : MonoBehaviour
{
    [SerializeField] private GrayboxPlayerController controller;

    private Animator animator;

    private void Awake()
    {
        animator = GetComponent<Animator>();

        if (controller == null)
        {
            controller =
                GetComponentInParent<GrayboxPlayerController>();
        }
    }
    
    public void BeginRollInvincibility()
    {
        controller?.BeginRollInvincibility();
    }

    public void EndRollInvincibility()
    {
        controller?.EndRollInvincibility();
    }

    private void OnAnimatorMove()
    {
        if (animator == null || controller == null)
        {
            return;
        }

        if (!controller.IsRolling)
        {
            return;
        }

        controller.ApplyRollRootMotion(
            animator.deltaPosition
        );
    }
}