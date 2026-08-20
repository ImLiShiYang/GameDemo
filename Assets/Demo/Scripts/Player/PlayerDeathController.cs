using UnityEngine;

[RequireComponent(typeof(Health))]
public class PlayerDeathController : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Animator animator;

    [SerializeField]
    private GrayboxPlayerController playerController;

    [SerializeField]
    private GamePlayerAttack playerAttack;

    private Health health;
    private bool isDead;

    private static readonly int DeadHash =
        Animator.StringToHash("Dead");

    private static readonly int RollHash =
        Animator.StringToHash("Roll");

    private static readonly int FireHash =
        Animator.StringToHash("Fire");

    private static readonly int MoveXHash =
        Animator.StringToHash("MoveX");

    private static readonly int MoveYHash =
        Animator.StringToHash("MoveY");

    private void Awake()
    {
        health = GetComponent<Health>();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        if (playerController == null)
        {
            playerController = GetComponent<GrayboxPlayerController>();
        }

        if (playerAttack == null)
        {
            playerAttack = GetComponent<GamePlayerAttack>();
        }
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Died += PlayDeath;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= PlayDeath;
        }
    }

    private void PlayDeath()
    {
        if (isDead)
        {
            return;
        }

        isDead = true;

        if (animator != null)
        {
            // 清除正在播放或等待播放的其他动作。
            animator.ResetTrigger(RollHash);

            animator.SetBool(FireHash, false);
            animator.SetFloat(MoveXHash, 0f);
            animator.SetFloat(MoveYHash, 0f);

            // 进入死亡状态。
            animator.SetBool(DeadHash, true);
        }

        // 死亡后禁止玩家继续移动、瞄准和翻滚。
        if (playerController != null)
        {
            // 禁用控制器前先关闭 IK，并把枪挂到右手骨骼下，
            // 让枪在死亡动画中继续跟随右手。
            playerController.EnterDeathWeaponState();
            playerController.enabled = false;
        }

        // 死亡后禁止继续发射子弹。
        if (playerAttack != null)
        {
            playerAttack.enabled = false;
        }

        // 隐藏玩家瞄准线。
        LineRenderer aimLine =
            GetComponentInChildren<LineRenderer>(true);

        if (aimLine != null)
        {
            aimLine.enabled = false;
        }
    }
}
