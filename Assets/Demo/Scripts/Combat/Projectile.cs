using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField]
    private float speed = 15f;

    [SerializeField]
    private float lifeTime = 3f;
    public float NetworkSpeed => speed;
    public float NetworkLifetime => lifeTime;
    
    [Header("碰撞")]
    [SerializeField]
    private LayerMask hitMask;

    private static readonly RaycastHit[] CastHits = new RaycastHit[16];

    private Vector3 moveDirection;
    private float damage;
    private GameObject owner;

    private Rigidbody projectileRigidbody;
    private SphereCollider projectileCollider;
    private TrailRenderer[] trailRenderers;
    private Vector3 previousPosition;

    private bool initialized;
    private bool hasHit;
    private float releaseTime;
    private PooledObject pooledObject;
    
    private int remainingPierces;

    private readonly HashSet<IDamageable> hitDamageables =new HashSet<IDamageable>();

    private void Awake()
    {
        projectileRigidbody = GetComponent<Rigidbody>();
        projectileCollider = GetComponent<SphereCollider>();
        trailRenderers = GetComponentsInChildren<TrailRenderer>(true);

        projectileRigidbody.useGravity = false;
        projectileRigidbody.isKinematic = true;
        previousPosition = projectileRigidbody.position;
    }

    public void Initialize(Vector3 direction,float damageAmount,GameObject projectileOwner,int pierceCount = 0)
    {
        if (direction.sqrMagnitude < 0.001f)
        {
            Debug.LogWarning(
                "Projectile received an invalid movement direction."
            );

            ReleaseSelf();

            return;
        }

        moveDirection = direction.normalized;

        damage = Mathf.Max(
            0f,
            damageAmount
        );

        owner = projectileOwner;

        remainingPierces =
            Mathf.Max(0, pierceCount);

        hitDamageables.Clear();

        hasHit = false;

        initialized = true;

        previousPosition =
            projectileRigidbody.position;

        releaseTime =
            Time.time + lifeTime;

        ClearTrails();

        transform.forward = moveDirection;
    }

    private void FixedUpdate()
    {
        if (!initialized || hasHit)
        {
            return;
        }

        if (Time.time >= releaseTime)
        {
            ReleaseSelf();
            return;
        }

        previousPosition = projectileRigidbody.position;
        float travelDistance = speed * Time.fixedDeltaTime;

        if (TryGetSphereCastHit(previousPosition,travelDistance,out RaycastHit hit))
        {
            HandleHit(hit.collider, hit.point, hit.normal);
            return;
        }

        projectileRigidbody.MovePosition(
            previousPosition + moveDirection * travelDistance);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!initialized || hasHit || other == null || other.isTrigger)
        {
            return;
        }

        if (IsOwnerCollider(other))
        {
            return;
        }

        // Fallback for collisions that occur outside the SphereCast path.
        ResolveFallbackHitPose(other, out Vector3 hitPoint, out Vector3 hitNormal);
        HandleHit(other, hitPoint, hitNormal);
    }

    private bool TryGetSphereCastHit(Vector3 origin,float travelDistance,out RaycastHit closestHit)
    {
        closestHit = default;

        if (projectileCollider == null || travelDistance <= 0f)
        {
            return false;
        }

        Vector3 scale = transform.lossyScale;
        float largestScale = Mathf.Max(
            Mathf.Abs(scale.x),
            Mathf.Abs(scale.y),
            Mathf.Abs(scale.z));
        float radius = projectileCollider.radius * largestScale;

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            radius,
            moveDirection,
            CastHits,
            travelDistance,
            hitMask,
            QueryTriggerInteraction.Ignore);

        float closestDistance = float.PositiveInfinity;

        for (int index = 0; index < hitCount; index++)
        {
            RaycastHit candidate = CastHits[index];
            Collider candidateCollider = candidate.collider;
            
            IDamageable candidateDamageable =candidateCollider.GetComponentInParent<IDamageable>();

            if (candidateDamageable != null &&hitDamageables.Contains(candidateDamageable))
            {
                continue;
            }

            if (candidateCollider == null ||
                candidateCollider == projectileCollider ||
                IsOwnerCollider(candidateCollider) ||
                candidate.distance >= closestDistance)
            {
                continue;
            }

            closestDistance = candidate.distance;
            closestHit = candidate;
        }

        return closestDistance < float.PositiveInfinity;
    }

    private void HandleHit(Collider hitCollider,Vector3 hitPoint,Vector3 hitNormal)
    {
        if (hasHit ||
            hitCollider == null ||
            IsOwnerCollider(hitCollider))
        {
            return;
        }

        if (hitNormal.sqrMagnitude < 0.0001f)
        {
            hitNormal = -moveDirection;
        }
        else
        {
            hitNormal.Normalize();
        }

        IDamageable damageable =
            hitCollider.GetComponentInParent<IDamageable>();

        /*
         * 撞到墙壁、地面等非伤害对象：
         * 直接结束。
         */
        if (damageable == null)
        {
            hasHit = true;
            ReleaseSelf();

            return;
        }

        /*
         * 同一个敌人已经被当前子弹命中过。
         */
        if (hitDamageables.Contains(damageable))
        {
            return;
        }

        /*
         * 死亡目标第一版仍然视为阻挡。
         */
        if (damageable.IsDead)
        {
            hasHit = true;
            ReleaseSelf();

            return;
        }

        hitDamageables.Add(damageable);

        DamageInfo damageInfo =
            new DamageInfo(
                damage,
                owner,
                hitPoint,
                moveDirection,
                hitNormal
            );

        damageable.TakeDamage(damageInfo);

        /*
         * 还有穿透次数：
         * 不回对象池，让子弹继续向前飞。
         */
        if (remainingPierces > 0)
        {
            remainingPierces--;

            return;
        }

        /*
         * 没有穿透次数了。
         */
        hasHit = true;

        ReleaseSelf();
    }

    private void ReleaseSelf()
    {
        initialized = false;

        if (pooledObject == null)
        {
            pooledObject = GetComponent<PooledObject>();
        }

        if (pooledObject != null)
        {
            pooledObject.Release();
            return;
        }

        // 兼容未通过对象池创建的测试实例。
        Destroy(gameObject);
    }

    private void OnDisable()
    {
        ClearTrails();
        initialized = false;
        hasHit = false;
        owner = null;
        damage = 0f;
        remainingPierces = 0;
        hitDamageables.Clear();
    }

    private void ClearTrails()
    {
        if (trailRenderers == null)
        {
            return;
        }

        foreach (TrailRenderer trailRenderer in trailRenderers)
        {
            if (trailRenderer != null)
            {
                trailRenderer.Clear();
            }
        }
    }

    private bool IsOwnerCollider(Collider targetCollider)
    {
        return targetCollider != null &&
               owner != null &&
               targetCollider.transform.root == owner.transform.root;
    }

    private void ResolveFallbackHitPose(
        Collider hitCollider,
        out Vector3 hitPoint,
        out Vector3 hitNormal)
    {
        Ray fallbackRay = new Ray(
            previousPosition - moveDirection * 0.5f,
            moveDirection);

        if (hitCollider.Raycast(fallbackRay, out RaycastHit hit, 1.5f))
        {
            hitPoint = hit.point;
            hitNormal = hit.normal;
            return;
        }

        hitPoint = projectileRigidbody.position;
        hitNormal = -moveDirection;
    }
}
