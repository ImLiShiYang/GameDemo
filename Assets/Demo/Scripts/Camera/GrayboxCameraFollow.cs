using UnityEngine;

public class GrayboxCameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;

    [SerializeField]
    private Vector3 offset = new Vector3(0f, 13f, -10f);

    [SerializeField] private float focusHeight = 1f;
    [SerializeField] private float smoothTime = 0.12f;

    private Vector3 followVelocity;

    private void LateUpdate()
    {
        if (target == null)
        {
            return;
        }

        Vector3 desiredPosition = target.position + offset;

        transform.position = Vector3.SmoothDamp(
            transform.position,
            desiredPosition,
            ref followVelocity,
            smoothTime
        );

        Vector3 focusPoint =
            target.position + Vector3.up * focusHeight;

        transform.LookAt(focusPoint);
    }
}