using UnityEngine;

public class ExperienceOrb : MonoBehaviour
{
    [Header("拾取")]
    [Tooltip("玩家进入这个范围后，经验球开始飞向玩家。")]
    [SerializeField, Min(0.1f)]
    private float pickupRadius = 3f;

    [Tooltip("距离玩家多近时认为已经拾取。")]
    [SerializeField, Min(0.01f)]
    private float collectDistance = 0.25f;

    [Header("视觉效果")]
    [SerializeField]
    private float rotateSpeed = 90f;
    
    [Header("吸附")]
    [SerializeField, Min(0.1f)]
    private float startMoveSpeed = 3f;

    [SerializeField, Min(0.1f)]
    private float maxMoveSpeed = 12f;

    [SerializeField, Min(0f)]
    private float acceleration = 20f;

    [Tooltip("经验球飞向玩家身体的大概高度。")]
    [SerializeField]
    private float targetHeight = 1f;

    private Transform player;
    private PlayerExperience playerExperience;

    private int experienceAmount;

    private bool isAttracted;

    private float currentMoveSpeed;

    /// <summary>
    /// 经验球生成后进行初始化。
    /// </summary>
    public void Initialize(
        Transform targetPlayer,
        PlayerExperience experience,
        int amount)
    {
        player = targetPlayer;
        playerExperience = experience;
        experienceAmount = amount;

        isAttracted = false;
        currentMoveSpeed = startMoveSpeed;
    }

    private void Update()
    {
        if (player == null || playerExperience == null)
        {
            return;
        }

        //经验球自动旋转
        transform.Rotate(Vector3.up,rotateSpeed * Time.deltaTime,Space.World);
        
        Vector3 targetPosition =
            player.position + Vector3.up * targetHeight;

        float distanceSqr =
            (targetPosition - transform.position).sqrMagnitude;

        /*
         * 还没有进入拾取范围时，
         * 经验球就待在地面附近。
         */
        if (!isAttracted)
        {
            if (distanceSqr >
                pickupRadius * pickupRadius)
            {
                return;
            }

            isAttracted = true;
        }

        /*
         * 吸向玩家时逐渐加速。
         */
        currentMoveSpeed = Mathf.MoveTowards(
            currentMoveSpeed,
            maxMoveSpeed,
            acceleration * Time.deltaTime
        );

        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPosition,
            currentMoveSpeed * Time.deltaTime
        );

        /*
         * 足够接近玩家，完成拾取。
         */
        if ((targetPosition - transform.position).sqrMagnitude <=
            collectDistance * collectDistance)
        {
            Collect();
        }
    }

    private void Collect()
    {
        playerExperience.AddExperience(experienceAmount);

        /*
         * 你的项目已经有 PooledObject。
         *
         * 以后如果把经验球接进对象池，
         * 这里可以直接兼容。
         *
         * 目前没有放进池子则直接 Destroy。
         */
        PooledObject pooledObject =GetComponent<PooledObject>();

        if (pooledObject != null)
        {
            pooledObject.Release();
        }
        else
        {
            Destroy(gameObject);
        }
    }
}