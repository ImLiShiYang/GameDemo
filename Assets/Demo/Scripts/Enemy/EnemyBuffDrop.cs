using UnityEngine;

[RequireComponent(typeof(Health))]
public class EnemyBuffDrop : MonoBehaviour
{
    [Header("Buff 掉落")]
    [SerializeField, Range(0f, 1f)]
    private float dropChance = 0.1f;

    [SerializeField]
    private BuffPickup speedBuffPrefab;

    [SerializeField]
    private BuffPickup shieldBuffPrefab;

    [Header("生成位置")]
    [SerializeField]
    private float spawnHeight = 0.3f;

    [SerializeField, Min(0f)]
    private float randomSpawnRadius = 0.3f;

    private Health health;
    private Transform player;

    private void Awake()
    {
        health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Died += TryDropBuff;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= TryDropBuff;
        }
    }

    private void TryDropBuff()
    {
        if (Random.value > dropChance)
        {
            return;
        }

        FindPlayer();

        if (player == null)
        {
            return;
        }

        BuffPickup prefab = SelectRandomBuff();

        if (prefab == null)
        {
            Debug.LogWarning($"{name} 没有配置 Buff Pickup Prefab。", this);
            return;
        }

        Vector2 randomOffset =
            Random.insideUnitCircle * randomSpawnRadius;

        Vector3 spawnPosition =
            transform.position +
            new Vector3(
                randomOffset.x,
                spawnHeight,
                randomOffset.y
            );

        BuffPickup pickup = Instantiate(
            prefab,
            spawnPosition,
            Quaternion.identity
        );

        pickup.Initialize(player);
    }

    private BuffPickup SelectRandomBuff()
    {
        if (speedBuffPrefab == null)
        {
            return shieldBuffPrefab;
        }

        if (shieldBuffPrefab == null)
        {
            return speedBuffPrefab;
        }

        return Random.value < 0.5f
            ? speedBuffPrefab
            : shieldBuffPrefab;
    }

    private void FindPlayer()
    {
        if (player != null)
        {
            return;
        }

        GameObject playerObject =
            GameObject.FindGameObjectWithTag("Player");

        if (playerObject != null)
        {
            player = playerObject.transform;
        }
    }
}