using UnityEngine;

public class BuffPickup : MonoBehaviour
{
    [Header("Buff")]
    [SerializeField]
    private string buffId;

    [Header("拾取")]
    [Tooltip("玩家进入这个范围后，Buff 开始飞向玩家。")]
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

    [Tooltip("Buff 飞向玩家身体的大概高度。")]
    [SerializeField]
    private float targetHeight = 1f;

    private Transform player;
    private bool isAttracted;
    private bool collected;
    private float currentMoveSpeed;
    private PooledObject pooledObject;

    private void OnDisable()
    {
        player = null;
        isAttracted = false;
        collected = false;
        currentMoveSpeed = startMoveSpeed;
    }

    public void Initialize(Transform targetPlayer)
    {
        player = targetPlayer;
        isAttracted = false;
        collected = false;
        currentMoveSpeed = startMoveSpeed;
    }

    private void Update()
    {
        if (player == null || collected)
        {
            return;
        }

        // transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);

        Vector3 targetPosition = player.position + Vector3.up * targetHeight;
        float distanceSqr = (targetPosition - transform.position).sqrMagnitude;

        if (!isAttracted)
        {
            if (distanceSqr > pickupRadius * pickupRadius)
            {
                return;
            }

            isAttracted = true;
        }

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

        if ((targetPosition - transform.position).sqrMagnitude <=
            collectDistance * collectDistance)
        {
            Collect();
        }
    }

    private void Collect()
    {
        if (collected)
        {
            return;
        }

        BuffManager buffManager = GameEntry.Buff;

        if (buffManager == null)
        {
            Debug.LogWarning("GameEntry.Buff 不存在，无法拾取 Buff。", this);
            return;
        }

        collected = true;

        buffManager.AddBuff(buffId);

        ReleaseSelf();
    }

    private void ReleaseSelf()
    {
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
}
