using UnityEngine;

/// <summary>直接引用单机原始预制体，Resources 保证独立客户端构建也包含这些资源。</summary>
public sealed class NetworkEnemyPrefabs : ScriptableObject
{
    public GameObject Melee;
    public GameObject Ranged;
    public GameObject Boss;
    public GameObject Projectile;
}
