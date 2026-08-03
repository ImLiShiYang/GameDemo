using UnityEngine;

public class RollStateExitBehaviour : StateMachineBehaviour
{
    public override void OnStateExit(
        Animator animator,
        AnimatorStateInfo stateInfo,
        int layerIndex)
    {
        GrayboxPlayerController controller =
            animator.GetComponentInParent<
                GrayboxPlayerController>();

        if (controller != null)
        {
            controller.FinishRoll();
        }
    }
}