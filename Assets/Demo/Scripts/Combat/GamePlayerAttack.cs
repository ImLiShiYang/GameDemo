using UnityEngine;

public class GamePlayerAttack : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Transform muzzle;

    [SerializeField]
    private Projectile projectilePrefab;
    
    [SerializeField]
    private PlayerCombatStats combatStats;

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
        CreateFallbackMuzzleFlash();
        InitializeShotAudio();

        if (combatStats == null)
        {
            combatStats = GetComponent<PlayerCombatStats>();
        }

        if (muzzleFlashEffect != null)
        {
            // Prevent Play On Awake from flashing once when the scene starts.
            muzzleFlashEffect.SetActive(false);
        }
    }

    private void Start()
    {
        PoolManager poolManager = GameEntry.Pool;

        if (poolManager == null)
        {
            return;
        }

        poolManager.WarmBulletPool(projectilePrefab);
        poolManager.WarmVFXPool(muzzleFlashEffect);
    }

    /// <summary>
    /// 尝试进行一次攻击。
    /// aimPoint 是鼠标瞄准到的世界坐标。
    /// 这里会根据 PlayerCombatStats 决定射击间隔、子弹数量、扩散角度和穿透次数。
    /// </summary>
    public void TryAttack(Vector3 aimPoint)
    {
        if (Time.timeScale <= 0f)
        {
            return;
        }

        // 还没到下一次允许攻击的时间，直接返回。
        if (Time.time < nextAttackTime)
        {
            return;
        }

        // 没有配置枪口位置，无法生成子弹。
        if (muzzle == null)
        {
            Debug.LogError("GamePlayerAttack 没有设置 Muzzle。",this);
            return;
        }

        // 没有配置子弹预制体，无法攻击。
        if (projectilePrefab == null)
        {
            Debug.LogError(
                "GamePlayerAttack 没有设置 Projectile Prefab。",
                this
            );

            return;
        }

        // 计算“枪口 → 鼠标瞄准点”的射击方向。
        Vector3 direction =aimPoint - muzzle.position;

        // 距离过小，方向没有意义，直接返回。
        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        // 只保留方向，不保留原来的长度。
        direction.Normalize();

        // 读取射击间隔倍率；没有 CombatStats 时按默认 1 倍处理。
        float intervalMultiplier =combatStats != null? combatStats.FireIntervalMultiplier: 1f;

        // 计算下一次允许攻击的时间。
        // 例如 attackInterval = 0.2，倍率 = 0.85，则实际间隔 = 0.17 秒。
        nextAttackTime =
            Time.time +
            attackInterval * intervalMultiplier;

        // 获取全局对象池，用于生成子弹。
        PoolManager poolManager = GameEntry.Pool;

        if (poolManager == null)
        {
            return;
        }

        // 读取一次攻击要生成多少颗子弹；默认 1 颗。
        int projectileCount =
            combatStats != null
                ? combatStats.ProjectileCount
                : 1;

        // 读取多颗子弹的总扩散角度；默认 0°。
        float spreadAngle =
            combatStats != null
                ? combatStats.SpreadAngle
                : 0f;

        // 读取子弹额外穿透次数；默认不穿透。
        int pierceCount =
            combatStats != null
                ? combatStats.PierceCount
                : 0;

        // 根据 ProjectileCount 循环生成对应数量的子弹。
        for (int i = 0; i < projectileCount; i++)
        {
            float angle = 0f;

            // 多颗子弹时，把它们均匀分布在总扩散角度范围内。
            if (projectileCount > 1)
            {
                // 把当前子弹索引映射到 0~1。
                // 例如 3 颗子弹时：0、0.5、1。
                float t =
                    i / (float)(projectileCount - 1);

                // 计算当前子弹对应的角度。
                // 例如 spreadAngle = 20°，3颗子弹：
                // -10°、0°、+10°。
                angle = Mathf.Lerp(
                    -spreadAngle * 0.5f,
                    spreadAngle * 0.5f,
                    t
                );
            }

            // 在原始瞄准方向基础上，绕 Y 轴旋转 angle 度。
            Vector3 shotDirection =
                Quaternion.AngleAxis(
                    angle,
                    Vector3.up
                ) * direction;

            // 从对象池生成子弹，并把方向和穿透次数传进去。
            SpawnProjectile(
                poolManager,
                shotDirection,
                pierceCount
            );
        }

        // 一次攻击无论生成几颗子弹，枪口特效和声音都只播放一次。
        PlayMuzzleFlash();
        PlayShotSound();
    }
    
    private void SpawnProjectile(
        PoolManager poolManager,
        Vector3 direction,
        int pierceCount)
    {
        Projectile projectile =
            poolManager.GetBullet(
                projectilePrefab,
                muzzle.position,
                Quaternion.LookRotation(direction)
            );

        if (projectile == null)
        {
            return;
        }

        projectile.Initialize(
            direction,
            damage,
            gameObject,
            pierceCount
        );
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

        PoolManager poolManager = GameEntry.Pool;

        if (poolManager == null)
        {
            return;
        }

        Transform templateTransform = muzzleFlashEffect.transform;

        GameObject effectInstance = poolManager.GetVFX(
            muzzleFlashEffect,
            templateTransform.position,
            templateTransform.rotation,
            muzzleFlashLifetime,
            templateTransform.parent
        );

        if (effectInstance == null)
        {
            return;
        }

        Transform effectTransform = effectInstance.transform;

        effectTransform.SetLocalPositionAndRotation(
            templateTransform.localPosition,
            templateTransform.localRotation);
        effectTransform.localScale = templateTransform.localScale;

        // GetVFX 已经激活实例，并会在 muzzleFlashLifetime 后自动回池。
    }

    private void InitializeShotAudio()
    {
        GameObject audioHost = muzzle != null
            ? muzzle.gameObject
            : gameObject;

        shotAudioSource = audioHost.GetComponent<AudioSource>();

        if (shotAudioSource == null)
        {
            shotAudioSource = audioHost.AddComponent<AudioSource>();
        }

        shotAudioSource.playOnAwake = false;
        shotAudioSource.spatialBlend = 0.35f;
        shotAudioSource.dopplerLevel = 0f;
    }

    private void CreateFallbackMuzzleFlash()
    {
        if (muzzleFlashEffect != null || muzzle == null)
        {
            return;
        }

        GameObject effectRoot =
            new GameObject("Runtime Muzzle Flash");

        effectRoot.transform.SetParent(muzzle, false);

        ParticleSystem particleSystem =
            effectRoot.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main =
            particleSystem.main;

        main.duration = 0.08f;
        main.loop = false;
        main.startLifetime = 0.06f;
        main.startSpeed = 0.5f;
        main.startSize = 0.22f;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.45f, 0.05f, 1f),
            new Color(1f, 0.95f, 0.45f, 1f)
        );

        ParticleSystem.EmissionModule emission =
            particleSystem.emission;

        emission.rateOverTime = 0f;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, 2, 3)
        });

        particleSystem.Stop(
            true,
            ParticleSystemStopBehavior.StopEmittingAndClear
        );

        muzzleFlashEffect = effectRoot;
        muzzleFlashLifetime = 0.12f;
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