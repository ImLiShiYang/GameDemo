using TMPro;
using UnityEngine;

[RequireComponent(typeof(TextMeshPro))]
public class DamageNumber : MonoBehaviour
{
    [SerializeField, Min(0.01f)]
    private float lifeTime = 0.7f;

    [SerializeField]
    private float moveSpeed = 1f;

    [SerializeField]
    private float horizontalSpread = 0.15f;

    private TextMeshPro damageText;
    private Camera mainCamera;

    private Color originalColor;
    private Vector3 moveDirection;
    private float elapsedTime;

    private void Awake()
    {
        damageText = GetComponent<TextMeshPro>();
        mainCamera = Camera.main;
        originalColor = damageText.color;
    }

    public void Initialize(float damageAmount)
    {
        damageText.text = Mathf.RoundToInt(damageAmount).ToString();

        moveDirection = new Vector3(
            Random.Range(-horizontalSpread, horizontalSpread),
            1f,
            Random.Range(-horizontalSpread, horizontalSpread)
        ).normalized;

        Destroy(gameObject, lifeTime);
    }

    private void Update()
    {
        elapsedTime += Time.deltaTime;

        transform.position +=
            moveDirection * moveSpeed * Time.deltaTime;

        FaceCamera();
        UpdateAlpha();
    }

    private void FaceCamera()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(
            transform.position - mainCamera.transform.position,
            Vector3.up
        );
    }

    private void UpdateAlpha()
    {
        float progress = Mathf.Clamp01(
            elapsedTime / lifeTime
        );

        Color currentColor = originalColor;
        currentColor.a = 1f - progress;

        damageText.color = currentColor;
    }
}