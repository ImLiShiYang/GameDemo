using UnityEngine;

public class PlayerAnimationEvents : MonoBehaviour
{
    [SerializeField]
    private GrayboxPlayerController playerController;

    private void Awake()
    {
        if (playerController == null)
        {
            playerController =
                GetComponentInParent<GrayboxPlayerController>();
        }
    }

    /// <summary>
    /// Hit Reaction 动画事件调用。
    /// </summary>
    public void FinishHitReaction()
    {
        if (playerController != null)
        {
            playerController.FinishHitReaction();
        }
    }
}