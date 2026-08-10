using UnityEngine;

[RequireComponent(typeof(Health))]
public class EnemyExperienceDrop : MonoBehaviour
{
    [Header("经验掉落")]
    [SerializeField]
    private ExperienceOrb experienceOrbPrefab;

    [SerializeField, Min(1)]
    private int experienceAmount = 10;

    [Header("生成位置")]
    [Tooltip("经验球生成时稍微高于地面。")]
    [SerializeField]
    private float spawnHeight = 0.3f;

    [Tooltip("让经验球不要永远生成在完全相同的位置。")]
    [SerializeField, Min(0f)]
    private float randomSpawnRadius = 0.3f;

    private Health health;

    private Transform player;

    private PlayerExperience playerExperience;

    private void Awake()
    {
        health = GetComponent<Health>();
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.Died += DropExperience;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.Died -= DropExperience;
        }
    }

    private void DropExperience()
    {
        if (experienceOrbPrefab == null)
        {
            Debug.LogWarning(
                $"{name} 没有配置 Experience Orb Prefab。",
                this
            );

            return;
        }

        /*
         * 第一次死亡时寻找玩家。
         *
         * 你的 EnemyAIController 当前也是通过
         * Player Tag 找玩家，所以这里保持一致。
         */
        if (player == null || playerExperience == null)
        {
            GameObject playerObject =
                GameObject.FindGameObjectWithTag("Player");

            if (playerObject == null)
            {
                Debug.LogWarning(
                    "场景中没有找到 Tag 为 Player 的对象。",
                    this
                );

                return;
            }

            player = playerObject.transform;

            playerExperience =
                playerObject.GetComponent<PlayerExperience>();

            if (playerExperience == null)
            {
                Debug.LogWarning(
                    $"{playerObject.name} 上没有 PlayerExperience。",
                    playerObject
                );

                return;
            }
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

        ExperienceOrb orb = Instantiate(
            experienceOrbPrefab,
            spawnPosition,
            Quaternion.identity
        );

        orb.Initialize(
            player,
            playerExperience,
            experienceAmount
        );

        Debug.Log(
            $"{name} 死亡，掉落 {experienceAmount} 点经验。",
            this
        );
    }
}