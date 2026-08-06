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

    [Header("Hit Effect")]
    [SerializeField, Min(0.01f)]
    private float hitEffectLifeTime = 1f;

    [Tooltip("Keeps the decal slightly in front of the hit surface.")]
    [SerializeField, Min(0f)]
    private float hitEffectSurfaceOffset = 0.002f;

    [Tooltip("Lifetime used when the hit effect contains an FPS_Decal bullet hole.")]
    [SerializeField, Min(0.1f)]
    private float bulletHoleLifeTime = 20f;

    [Header("Hit Flash")]
    [Tooltip("URP Lit 通常使用 _BaseColor；其他 Shader 可能使用 _Color。")]
    [SerializeField]
    private string colorPropertyName = "_BaseColor";

    [SerializeField]
    private Color flashColor = Color.white;

    [SerializeField, Min(0.01f)]
    private float flashDuration = 0.08f;

    [Header("Hit Sound")]
    [Tooltip("包含受击声音的完整音频文件。")]
    [SerializeField]
    private AudioClip hitSoundClip;

    [Tooltip("从完整音频的第几秒开始播放。")]
    [SerializeField, Min(0f)]
    private float hitSoundStartTime = 0f;

    [Tooltip("从开始位置播放多长时间。")]
    [SerializeField, Min(0.01f)]
    private float hitSoundDuration = 1f;

    [Tooltip("受击声音音量。")]
    [SerializeField, Range(0f, 1f)]
    private float hitSoundVolume = 1f;

    [Tooltip("可选。未指定时自动创建一个 AudioSource。")]
    [SerializeField]
    private AudioSource hitAudioSource;

    [Tooltip("0 表示 2D 声音，1 表示从怪物位置发出的 3D 声音。")]
    [SerializeField, Range(0f, 1f)]
    private float hitSoundSpatialBlend = 1f;

    [Tooltip("3D 声音在该距离内保持最大音量。")]
    [SerializeField, Min(0f)]
    private float hitSoundMinDistance = 1f;

    [Tooltip("3D 声音的最大传播距离。")]
    [SerializeField, Min(0.01f)]
    private float hitSoundMaxDistance = 20f;

    private Health health;

    private Coroutine flashCoroutine;
    private Coroutine hitSoundCoroutine;

    private static readonly int BaseColorPropertyId =
        Shader.PropertyToID("_BaseColor");
    private static readonly int ColorPropertyId =
        Shader.PropertyToID("_Color");
    private static readonly int TintColorPropertyId =
        Shader.PropertyToID("_TintColor");
    private readonly List<FlashTarget> flashTargets = new();

    private sealed class FlashTarget
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public int ColorPropertyId;
        public Color OriginalColor;
        public MaterialPropertyBlock PropertyBlock;
    }

    private void Awake()
    {
        health = GetComponent<Health>();

        PrepareHitAudioSource();

        if (targetRenderers == null || targetRenderers.Length == 0)
        {
            targetRenderers = GetComponentsInChildren<Renderer>(true);
        }

        CacheFlashTargets();

        if (flashTargets.Count == 0)
        {
            Debug.LogWarning(
                $"DamageFeedback on {name} found no usable color property for hit flash.");
        }
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

        if (hitSoundCoroutine != null)
        {
            StopCoroutine(hitSoundCoroutine);
            hitSoundCoroutine = null;
        }

        if (hitAudioSource != null)
        {
            hitAudioSource.Stop();
        }

        RestoreOriginalColors();
    }

    private void PrepareHitAudioSource()
    {
        if (hitAudioSource == null)
        {
            hitAudioSource = GetComponent<AudioSource>();
        }

        if (hitAudioSource == null)
        {
            hitAudioSource = gameObject.AddComponent<AudioSource>();
        }

        hitAudioSource.playOnAwake = false;
        hitAudioSource.loop = false;
        hitAudioSource.spatialBlend = hitSoundSpatialBlend;
        hitAudioSource.minDistance = hitSoundMinDistance;
        hitAudioSource.maxDistance = hitSoundMaxDistance;
        hitAudioSource.rolloffMode = AudioRolloffMode.Logarithmic;
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
                int propertyId = ResolveFlashColorPropertyId(material);

                if (propertyId == 0)
                {
                    continue;
                }

                flashTargets.Add(new FlashTarget
                {
                    Renderer = targetRenderer,
                    MaterialIndex = materialIndex,
                    ColorPropertyId = propertyId,
                    OriginalColor = material.GetColor(propertyId),
                    PropertyBlock = new MaterialPropertyBlock()
                });
            }
        }
    }

    private int ResolveFlashColorPropertyId(Material material)
    {
        if (material == null)
        {
            return 0;
        }

        int configuredPropertyId = Shader.PropertyToID(colorPropertyName);

        if (material.HasProperty(configuredPropertyId))
        {
            return configuredPropertyId;
        }

        if (material.HasProperty(BaseColorPropertyId))
        {
            return BaseColorPropertyId;
        }

        if (material.HasProperty(ColorPropertyId))
        {
            return ColorPropertyId;
        }

        return material.HasProperty(TintColorPropertyId)
            ? TintColorPropertyId
            : 0;
    }
    private void OnDamaged(DamageInfo damageInfo)
    {
        SpawnHitEffect(damageInfo);
        SpawnDamageNumber(damageInfo);
        // PlayHitFlash();
        PlayHitSound();
    }

    private void PlayHitSound()
    {
        if (hitAudioSource == null || hitSoundClip == null)
        {
            return;
        }

        if (hitSoundClip.length <= 0f)
        {
            return;
        }

        // 防止开始时间超过音频总长度。
        float startTime = Mathf.Clamp(
            hitSoundStartTime,
            0f,
            Mathf.Max(0f, hitSoundClip.length - 0.01f)
        );

        // 防止播放时长超过剩余音频长度。
        float playableDuration = Mathf.Clamp(
            hitSoundDuration,
            0.01f,
            hitSoundClip.length - startTime
        );

        // 连续受击时，重新从指定位置播放。
        if (hitSoundCoroutine != null)
        {
            StopCoroutine(hitSoundCoroutine);
            hitSoundCoroutine = null;
        }

        hitAudioSource.Stop();

        hitAudioSource.clip = hitSoundClip;
        hitAudioSource.volume = hitSoundVolume;
        hitAudioSource.pitch = 1f;
        hitAudioSource.loop = false;

        hitAudioSource.time = startTime;
        hitAudioSource.Play();

        hitSoundCoroutine = StartCoroutine(
            StopHitSoundAfterDelay(playableDuration)
        );
    }

    private IEnumerator StopHitSoundAfterDelay(float duration)
    {
        yield return new WaitForSeconds(duration);

        if (hitAudioSource != null)
        {
            hitAudioSource.Stop();
        }

        hitSoundCoroutine = null;
    }

    private void SpawnHitEffect(DamageInfo damageInfo)
    {
        if (hitEffectPrefab == null)
        {
            return;
        }

        Vector3 hitNormal = ResolveHitNormal(damageInfo);

        Quaternion rotation = Quaternion.LookRotation(hitNormal);

        Vector3 spawnPosition =
            damageInfo.HitPoint +
            hitNormal * hitEffectSurfaceOffset;

        GameObject effect = Instantiate(
            hitEffectPrefab,
            spawnPosition,
            rotation
        );

        bool hasBulletHole =
            effect.GetComponentInChildren<FPS_Decal>() != null;

        if (hasBulletHole)
        {
            foreach (FPSShaderColorGradient gradient in
                     effect.GetComponentsInChildren<FPSShaderColorGradient>())
            {
                gradient.enabled = false;
            }
        }

        float lifeTime = hasBulletHole
            ? bulletHoleLifeTime
            : hitEffectLifeTime;

        Destroy(effect, lifeTime);
    }

    private Vector3 ResolveHitNormal(DamageInfo damageInfo)
    {
        Vector3 hitNormal = damageInfo.HitNormal;

        if (hitNormal.sqrMagnitude < 0.0001f)
        {
            hitNormal = -damageInfo.HitDirection;
        }

        if (hitNormal.sqrMagnitude < 0.0001f)
        {
            hitNormal = transform.forward;
        }

        return hitNormal.normalized;
    }

    private void SpawnDamageNumber(DamageInfo damageInfo)
    {
        if (damageNumberPrefab == null)
        {
            return;
        }

        Vector3 hitNormal = ResolveHitNormal(damageInfo);

        Vector3 spawnPosition =
            damageInfo.HitPoint +
            hitNormal * damageNumberSurfaceOffset;

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
                target.ColorPropertyId,
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
                target.ColorPropertyId,
                target.OriginalColor
            );

            target.Renderer.SetPropertyBlock(
                target.PropertyBlock,
                target.MaterialIndex
            );
        }
    }
}