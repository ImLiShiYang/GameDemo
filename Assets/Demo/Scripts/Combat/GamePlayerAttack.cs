using UnityEngine;

public class GamePlayerAttack : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Transform muzzle;

    [SerializeField]
    private Projectile projectilePrefab;

    [Header("Muzzle Flash")]
    [Tooltip("Muzzle flash root placed under Muzzle. If empty, the first particle effect under Muzzle is used.")]
    [SerializeField]
    private GameObject muzzleFlashEffect;

    [SerializeField, Min(0.1f)]
    private float muzzleFlashLifetime = 2f;

    [Header("Audio")]
    [SerializeField]
    private AudioClip shotSound;

    [SerializeField, Range(0f, 1f)]
    private float shotVolume = 1f;

    [Header("Attack")]
    [SerializeField]
    private float damage = 10f;

    [SerializeField]
    private float attackInterval = 0.2f;

    private float nextAttackTime;

    private AudioSource shotAudioSource;


    private void Awake()
    {
        ResolveMuzzleFlashEffect();
        InitializeShotAudio();

        if (muzzleFlashEffect == null)
        {
            return;
        }

        // Prevent Play On Awake from flashing once when the scene starts.
        muzzleFlashEffect.SetActive(false);
    }

    public void TryAttack(Vector3 aimPoint)
    {
        if (Time.time < nextAttackTime)
        {
            return;
        }

        if (muzzle == null)
        {
            Debug.LogError("GamePlayerAttack 没有设置 Muzzle。");
            return;
        }

        if (projectilePrefab == null)
        {
            Debug.LogError("GamePlayerAttack 没有设置 Projectile Prefab。");
            return;
        }

        Vector3 direction = aimPoint - muzzle.position;

        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        nextAttackTime = Time.time + attackInterval;

        Projectile projectile = Instantiate(projectilePrefab,muzzle.position,Quaternion.LookRotation(direction.normalized));

        projectile.Initialize(direction, damage, gameObject);

        PlayMuzzleFlash();
        PlayShotSound();
            
    }

    private void ResolveMuzzleFlashEffect()
    {
        if (muzzleFlashEffect != null || muzzle == null)
        {
            return;
        }

        ParticleSystem particle =
            muzzle.GetComponentInChildren<ParticleSystem>(true);

        if (particle == null)
        {
            return;
        }

        Transform effectRoot = particle.transform;

        // Find the direct child of Muzzle so the whole effect is replayed.
        while (effectRoot.parent != null && effectRoot.parent != muzzle)
        {
            effectRoot = effectRoot.parent;
        }

        if (effectRoot.parent == muzzle)
        {
            muzzleFlashEffect = effectRoot.gameObject;
        }
    }

    private void PlayMuzzleFlash()
    {
        if (muzzleFlashEffect == null)
        {
            return;
        }

        GameObject effectInstance = Instantiate(
            muzzleFlashEffect,
            muzzleFlashEffect.transform.parent);

        Transform effectTransform = effectInstance.transform;
        Transform templateTransform = muzzleFlashEffect.transform;

        effectTransform.SetLocalPositionAndRotation(
            templateTransform.localPosition,
            templateTransform.localRotation);
        effectTransform.localScale = templateTransform.localScale;

        effectInstance.SetActive(true);
        Destroy(effectInstance, muzzleFlashLifetime);
    }

    private void InitializeShotAudio()
    {
        GameObject audioHost = muzzle != null
            ? muzzle.gameObject
            : gameObject;

        shotAudioSource = audioHost.AddComponent<AudioSource>();
        shotAudioSource.playOnAwake = false;
        shotAudioSource.spatialBlend = 1f;
        shotAudioSource.dopplerLevel = 0f;
    }

    private void PlayShotSound()
    {
        if (shotSound == null || shotAudioSource == null)
        {
            return;
        }

        shotAudioSource.PlayOneShot(shotSound, shotVolume);
    }
}