using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class Projectile : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField]
    private float speed = 15f;

    [SerializeField]
    private float lifeTime = 3f;

    private Vector3 moveDirection;
    private float damage;
    private GameObject owner;

    private Rigidbody projectileRigidbody;

    private bool initialized;
    private bool hasHit;

    private void Awake()
    {
        projectileRigidbody = GetComponent<Rigidbody>();

        projectileRigidbody.useGravity = false;
        projectileRigidbody.isKinematic = true;
    }

    /// <summary>
    /// 子弹生成后，由攻击者传入本次攻击数据。
    /// </summary>
    public void Initialize(Vector3 direction,float damageAmount,GameObject projectileOwner)
    {
        if (direction.sqrMagnitude < 0.001f)
        {
            Debug.LogWarning("Projectile 收到了无效的移动方向。");
            Destroy(gameObject);
            return;
        }

        moveDirection = direction.normalized;
        damage = Mathf.Max(0f, damageAmount);
        owner = projectileOwner;

        initialized = true;

        // 让子弹模型朝向飞行方向。
        transform.forward = moveDirection;

        // 防止没有碰到任何物体的子弹一直留在场景里。
        Destroy(gameObject, lifeTime);
    }

    private void FixedUpdate()
    {
        if (!initialized || hasHit)
        {
            return;
        }

        Vector3 nextPosition =
            projectileRigidbody.position +
            moveDirection * speed * Time.fixedDeltaTime;

        projectileRigidbody.MovePosition(nextPosition);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!initialized || hasHit)
        {
            return;
        }

        // 忽略发射子弹的玩家及其所有子物体。
        if (owner != null &&
            other.transform.root == owner.transform.root)
        {
            return;
        }

        // 从碰撞物体或其父物体上寻找伤害接口。
        IDamageable damageable =
            other.GetComponentInParent<IDamageable>();

        if (damageable != null && !damageable.IsDead)
        {
            hasHit = true;

            Vector3 hitPoint =
                other.ClosestPoint(transform.position);

            DamageInfo damageInfo = new DamageInfo(
                damage,
                owner,
                hitPoint,
                moveDirection);

            damageable.TakeDamage(damageInfo);

            Debug.Log(
                $"Projectile 命中：{other.name}，造成伤害：{damage}");

            Destroy(gameObject);
            return;
        }

        // 碰到地面或墙壁，即使目标不能受伤，也销毁子弹。
        if (!other.isTrigger)
        {
            hasHit = true;
            Destroy(gameObject);
        }
    }
}