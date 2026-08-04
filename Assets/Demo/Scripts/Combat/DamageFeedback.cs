using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Health))]
public class DamageFeedback : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Renderer[] targetRenderers;

    [SerializeField]
    private DamageNumber damageNumberPrefab;

    [SerializeField]
    private GameObject hitEffectPrefab;
    
    [Header("Damage Number")]
    [SerializeField]
    private float damageNumberSurfaceOffset = 0.25f;

    [SerializeField]
    private float damageNumberUpOffset = 0.15f;

    [Header("Hit Effect")]
    [SerializeField, Min(0.01f)]
    private float hitEffectLifeTime = 1f;

    [Header("Hit Flash")]
    [Tooltip("URP Lit 通常使用 _BaseColor；其他 Shader 可能使用 _Color。")]
    [SerializeField]
    private string colorPropertyName = "_BaseColor";

    [SerializeField]
    private Color flashColor = Color.white;

    [SerializeField, Min(0.01f)]
    private float flashDuration = 0.08f;

    private Health health;
    private Coroutine flashCoroutine;
    private int colorPropertyId;

    private readonly List<FlashTarget> flashTargets = new();

    private sealed class FlashTarget
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public Color OriginalColor;
        public MaterialPropertyBlock PropertyBlock;
    }

    private void Awake()
    {
        health = GetComponent<Health>();

        if (targetRenderers == null || targetRenderers.Length == 0)
        {
            targetRenderers = GetComponentsInChildren<Renderer>(true);
        }

        colorPropertyId = Shader.PropertyToID(colorPropertyName);
        CacheFlashTargets();
    }

    private void OnEnable()
    {
        health.Damaged += OnDamaged;
    }

    private void OnDisable()
    {
        health.Damaged -= OnDamaged;

        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }

        RestoreOriginalColors();
    }

    private void CacheFlashTargets()
    {
        flashTargets.Clear();

        foreach (Renderer targetRenderer in targetRenderers)
        {
            if (targetRenderer == null)
            {
                continue;
            }

            Material[] materials = targetRenderer.sharedMaterials;

            for (int materialIndex = 0;
                 materialIndex < materials.Length;
                 materialIndex++)
            {
                Material material = materials[materialIndex];

                if (material == null ||
                    !material.HasProperty(colorPropertyId))
                {
                    continue;
                }

                flashTargets.Add(new FlashTarget
                {
                    Renderer = targetRenderer,
                    MaterialIndex = materialIndex,
                    OriginalColor = material.GetColor(colorPropertyId),
                    PropertyBlock = new MaterialPropertyBlock()
                });
            }
        }
    }

    private void OnDamaged(DamageInfo damageInfo)
    {
        SpawnHitEffect(damageInfo);
        // SpawnDamageNumber(damageInfo);
        PlayHitFlash();
    }

    private void SpawnHitEffect(DamageInfo damageInfo)
    {
        if (hitEffectPrefab == null)
        {
            return;
        }

        Quaternion rotation = Quaternion.identity;

        if (damageInfo.HitDirection.sqrMagnitude > 0.0001f)
        {
            rotation = Quaternion.LookRotation(
                -damageInfo.HitDirection.normalized
            );
        }

        GameObject effect = Instantiate(hitEffectPrefab,damageInfo.HitPoint,rotation);
            
        Destroy(effect, hitEffectLifeTime);
    }

    private void SpawnDamageNumber(DamageInfo damageInfo)
    {
        if (damageNumberPrefab == null)
        {
            return;
        }

        Vector3 hitNormal = damageInfo.HitNormal;

        if (hitNormal.sqrMagnitude < 0.0001f)
        {
            hitNormal = -damageInfo.HitDirection;
        }

        hitNormal.Normalize();

        Vector3 spawnPosition =
            damageInfo.HitPoint +
            hitNormal * damageNumberSurfaceOffset +
            Vector3.up * damageNumberUpOffset;

        DamageNumber damageNumber = Instantiate(
            damageNumberPrefab,
            spawnPosition,
            Quaternion.identity
        );

        damageNumber.Initialize(damageInfo.Amount);
    }

    private void PlayHitFlash()
    {
        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
        }

        flashCoroutine = StartCoroutine(HitFlashRoutine());
    }

    private IEnumerator HitFlashRoutine()
    {
        SetFlashColor(flashColor);

        yield return new WaitForSeconds(flashDuration);

        RestoreOriginalColors();
        flashCoroutine = null;
    }

    private void SetFlashColor(Color color)
    {
        foreach (FlashTarget target in flashTargets)
        {
            if (target.Renderer == null)
            {
                continue;
            }

            target.Renderer.GetPropertyBlock(
                target.PropertyBlock,
                target.MaterialIndex
            );

            target.PropertyBlock.SetColor(
                colorPropertyId,
                color
            );

            target.Renderer.SetPropertyBlock(
                target.PropertyBlock,
                target.MaterialIndex
            );
        }
    }

    private void RestoreOriginalColors()
    {
        foreach (FlashTarget target in flashTargets)
        {
            if (target.Renderer == null)
            {
                continue;
            }

            target.Renderer.GetPropertyBlock(
                target.PropertyBlock,
                target.MaterialIndex
            );

            target.PropertyBlock.SetColor(
                colorPropertyId,
                target.OriginalColor
            );

            target.Renderer.SetPropertyBlock(
                target.PropertyBlock,
                target.MaterialIndex
            );
        }
    }
}