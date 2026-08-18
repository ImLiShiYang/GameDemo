using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class BillboardPickupQuad : MonoBehaviour
{
    [Header("Pulse")]
    [SerializeField]
    private bool randomPhaseOnEnable = true;

    [SerializeField, Range(0f, 6.28318f)]
    private float pulsePhase = 0f;

    private static readonly int PulsePhaseId =
        Shader.PropertyToID("_PulsePhase");

    private Camera targetCamera;
    private Renderer cachedRenderer;
    private MaterialPropertyBlock propertyBlock;

    private void Awake()
    {
        cachedRenderer = GetComponent<Renderer>();
        propertyBlock = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        if (randomPhaseOnEnable)
        {
            pulsePhase = Random.Range(0f, Mathf.PI * 2f);
        }

        ApplyPulsePhase();
    }

    private void LateUpdate()
    {
        FaceCamera();
    }

    private void FaceCamera()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            return;
        }

        transform.forward = targetCamera.transform.forward;
    }

    private void ApplyPulsePhase()
    {
        if (cachedRenderer == null)
        {
            cachedRenderer = GetComponent<Renderer>();
        }

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }

        cachedRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(PulsePhaseId, pulsePhase);
        cachedRenderer.SetPropertyBlock(propertyBlock);
    }
}