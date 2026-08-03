using UnityEngine;
using UnityEngine.Serialization;

public class WeaponLeftHandIK : MonoBehaviour
{
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
        if (animator == null || leftHandGrip == null)
            return;

        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, positionWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, rotationWeight);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandGrip.position);
        animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandGrip.rotation);
    }
}