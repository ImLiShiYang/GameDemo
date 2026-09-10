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
        // Root Motion 从骨骼姿势中提取，但不写回任何 Transform。
        // SimulationRoot 的位移只允许由 PlayerMovementSimulation 产生。
        if (animator == null) animator = GetComponent<Animator>();
    }

}
