using UnityEngine;
using UnityEngine.Serialization;

public class WeaponLeftHandIK : MonoBehaviour
{
    [Header("Legacy Animator IK")]
    [Tooltip("仅在没有使用 Animation Rigging 的旧场景中开启。当前 AimRig 场景必须保持关闭，避免两套 IK 同时控制左手。")]
    [SerializeField] private bool enableLegacyAnimatorIK;

    [SerializeField] private Animator animator;
    [SerializeField] private Transform leftHandGrip;

    [Range(0f, 1f)]
    [FormerlySerializedAs("leftHandPositionWeight")]
    [SerializeField] private float positionWeight = 1f;

    [Range(0f, 1f)]
    [FormerlySerializedAs("leftHandRotationWeight")]
    [SerializeField] private float rotationWeight;

    private void Reset()
    {
        animator = GetComponent<Animator>();
    }

    private void OnAnimatorIK(int layerIndex)
    {
        if (!enableLegacyAnimatorIK ||
            animator == null ||
            leftHandGrip == null)
        {
            return;
        }

        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, positionWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, rotationWeight);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandGrip.position);
        animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandGrip.rotation);
    }
}
