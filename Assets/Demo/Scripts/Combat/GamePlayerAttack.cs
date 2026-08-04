using UnityEngine;

public class GamePlayerAttack : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Transform muzzle;

    [SerializeField]
    private Projectile projectilePrefab;

    [Header("Attack")]
    [SerializeField]
    private float damage = 10f;

    [SerializeField]
    private float attackInterval = 0.2f;

    private float nextAttackTime;

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

        Projectile projectile = Instantiate(
            projectilePrefab,
            muzzle.position,
            Quaternion.LookRotation(direction.normalized));

        projectile.Initialize(direction,damage,gameObject);
            
            
            
    }
}