using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class EnemyHomingProjectile : MonoBehaviour
{
    [Header("移动")]
    [SerializeField, Min(0.1f)]
    private float speed = 6f;

    [Tooltip("每秒最多转向多少度。数值越大，追踪能力越强。")]
    [SerializeField, Min(0f)]
    private float turnSpeed = 180f;

    [Tooltip("飞弹相对于发射时的初始方向，最多允许偏转多少度。")]
    [SerializeField, Range(0f, 180f)]
    private float maxHomingAngle = 45f;

    [SerializeField, Min(0.1f)]
    private float lifeTime = 5f;

    [Tooltip("只在这段时间内追踪玩家，之后沿最后方向直线飞行。")]
    [SerializeField, Min(0f)]
    private float homingDuration = 2f;

    [Header("碰撞")]
    [SerializeField]
    private LayerMask hitMask;
    
    [Header("瞄准")]
    [Tooltip("目标没有 CharacterController 时，瞄准根节点上方的高度。")]
    [SerializeField]
    private float targetHeightOffset = 1f;

    private static readonly RaycastHit[] CastHits =new RaycastHit[16];

    private Rigidbody projectileRigidbody;
    private SphereCollider projectileCollider;
    private TrailRenderer[] trailRenderers;

    private Transform target;
    private CharacterController targetCharacterController;

    private Vector3 moveDirection;
    private Vector3 previousPosition;
    // 飞弹刚发射时的方向，用来限制最大转向角度
    private Vector3 initialMoveDirection;

    private float damage;
    private float homingEndTime;

    private GameObject owner;

    private bool initialized;
    private bool hasHit;
    private float releaseTime;
    private PooledObject pooledObject;

    private void Awake()
    {
        projectileRigidbody = GetComponent<Rigidbody>();
        projectileCollider = GetComponent<SphereCollider>();
        trailRenderers = GetComponentsInChildren<TrailRenderer>(true);

        projectileRigidbody.useGravity = false;
        projectileRigidbody.isKinematic = true;

        previousPosition = projectileRigidbody.position;
    }

    /// <summary>
    /// 生成魔法弹后传入目标、伤害和攻击者。
    /// </summary>
    public void Initialize(Transform attackTarget,float damageAmount,GameObject projectileOwner)
    {
        if (attackTarget == null)
        {
            Debug.LogWarning(
                $"{name} 没有收到有效的追踪目标。",
                this
            );

            ReleaseSelf();
            return;
        }

        target = attackTarget;
        damage = Mathf.Max(0f, damageAmount);
        owner = projectileOwner;

        targetCharacterController =
            target.GetComponent<CharacterController>();

        if (targetCharacterController == null)
        {
            targetCharacterController =
                target.GetComponentInChildren<CharacterController>();
        }

        Vector3 initialDirection =
            GetTargetPosition() -
            projectileRigidbody.position;

        if (initialDirection.sqrMagnitude < 0.0001f)
        {
            initialDirection = transform.forward;
        }

        moveDirection = initialDirection.normalized;
        
        // 记录发射瞬间方向
        initialMoveDirection = moveDirection;

        // transform.forward = moveDirection;
        projectileRigidbody.rotation =
            Quaternion.LookRotation(moveDirection, Vector3.up);

        previousPosition = projectileRigidbody.position;
        homingEndTime = Time.time + homingDuration;
        releaseTime = Time.time + lifeTime;

        hasHit = false;
        initialized = true;

        ClearTrails();
    }
    
    private bool IsInvinciblePlayer(Collider targetCollider)
    {
        if (targetCollider == null)
        {
            return false;
        }

        GrayboxPlayerController playerController =
            targetCollider.GetComponentInParent<GrayboxPlayerController>();

        return playerController != null &&
               playerController.IsInvincible;
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

        /*
         * 在追踪时间内，逐渐朝玩家转向。
         * 不是瞬间锁定，因此玩家仍然可以躲避。
         */
        if (target != null && Time.time <= homingEndTime)
        {
            UpdateHomingDirection();
        }

        float travelDistance =
            speed * Time.fixedDeltaTime;

        if (TryGetSphereCastHit(previousPosition,travelDistance,out RaycastHit hit))
        {
            HandleHit(hit.collider,hit.point,hit.normal);
            return;
        }

        Vector3 nextPosition = previousPosition +moveDirection * travelDistance;
        Quaternion nextRotation = Quaternion.LookRotation(
            moveDirection,
            Vector3.up
        );

        projectileRigidbody.MoveRotation(nextRotation);
        projectileRigidbody.MovePosition(nextPosition);

        // if (moveDirection.sqrMagnitude > 0.0001f)
        // {
        //     transform.forward = moveDirection;
        // }
    }

    private void UpdateHomingDirection()
    {
        Vector3 desiredDirection =
            GetTargetPosition() -
            projectileRigidbody.position;

        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        desiredDirection.Normalize();

        /*
         * 限制飞弹相对于“发射初始方向”的最大偏转角度。
         *
         * 比如 maxHomingAngle = 45：
         * 飞弹最多只能向左右偏转 45 度，
         * 不允许为了追玩家直接拐 90 度甚至掉头。
         */
        float angleFromInitial = Vector3.Angle(
            initialMoveDirection,
            desiredDirection
        );

        if (angleFromInitial > maxHomingAngle)
        {
            desiredDirection = Vector3.RotateTowards(
                initialMoveDirection,
                desiredDirection,
                maxHomingAngle * Mathf.Deg2Rad,
                0f
            );

            desiredDirection.Normalize();
        }

        /*
         * turnSpeed 控制转弯速度。
         */
        float maxRadiansDelta =
            turnSpeed *
            Mathf.Deg2Rad *
            Time.fixedDeltaTime;

        moveDirection = Vector3.RotateTowards(
            moveDirection,
            desiredDirection,
            maxRadiansDelta,
            0f
        );

        moveDirection.Normalize();
    }

    private Vector3 GetTargetPosition()
    {
        if (targetCharacterController != null)
        {
            return targetCharacterController.bounds.center;
        }

        if (target != null)
        {
            return target.position +
                   Vector3.up * targetHeightOffset;
        }

        return projectileRigidbody.position +
               moveDirection;
    }

    private bool TryGetSphereCastHit(Vector3 origin,float travelDistance,out RaycastHit closestHit)
    {
        closestHit = default;

        if (projectileCollider == null ||
            travelDistance <= 0f)
        {
            return false;
        }

        Vector3 scale = transform.lossyScale;

        float largestScale = Mathf.Max(
            Mathf.Abs(scale.x),
            Mathf.Abs(scale.y),
            Mathf.Abs(scale.z)
        );

        float radius =
            projectileCollider.radius *
            largestScale;

        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            radius,
            moveDirection,
            CastHits,
            travelDistance,
            hitMask,
            QueryTriggerInteraction.Ignore
        );

        float closestDistance =
            float.PositiveInfinity;

        for (int index = 0;
             index < hitCount;
             index++)
        {
            RaycastHit candidate =
                CastHits[index];

            Collider candidateCollider =
                candidate.collider;

            if (candidateCollider == null ||
                candidateCollider == projectileCollider ||
                IsOwnerCollider(candidateCollider) ||
                candidate.distance >= closestDistance)
            {
                continue;
            }
            
            // 玩家处于无敌帧，飞弹忽略玩家并继续飞行
            if (IsInvinciblePlayer(candidateCollider))
            {
                Debug.Log("魔法飞弹检测到玩家无敌帧，忽略碰撞，继续飞行");
                continue;
            }

            closestDistance =
                candidate.distance;

            closestHit = candidate;
        }

        return closestDistance <
               float.PositiveInfinity;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!initialized ||
            hasHit ||
            other == null ||
            other.isTrigger ||
            IsOwnerCollider(other)||
            IsInvinciblePlayer(other))
        {
            return;
        }

        Vector3 hitPoint =other.ClosestPoint(projectileRigidbody.position);

        Vector3 hitNormal =
            -moveDirection;

        HandleHit(
            other,
            hitPoint,
            hitNormal
        );
    }

    private void HandleHit(Collider hitCollider,Vector3 hitPoint,Vector3 hitNormal)
    {
        if (hasHit ||
            hitCollider == null ||
            IsOwnerCollider(hitCollider))
        {
            return;
        }

        hasHit = true;

        if (hitNormal.sqrMagnitude < 0.0001f)
        {
            hitNormal = -moveDirection;
        }
        else
        {
            hitNormal.Normalize();
        }
        

        IDamageable damageable =hitCollider.GetComponentInParent<IDamageable>();

        if (damageable != null &&!damageable.IsDead)
        {
            DamageInfo damageInfo =
                new DamageInfo(
                    damage,
                    owner,
                    hitPoint,
                    moveDirection,
                    hitNormal
                );

            damageable.TakeDamage(
                in damageInfo
            );
        }

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
        target = null;
        targetCharacterController = null;
        owner = null;
        damage = 0f;
        initialized = false;
        hasHit = false;
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

    private bool IsOwnerCollider(
        Collider targetCollider)
    {
        return targetCollider != null &&
               owner != null &&
               targetCollider.transform.root ==
               owner.transform.root;
    }
}