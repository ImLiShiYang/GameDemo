using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单个 Prefab 对应的通用对象池。
/// PoolManager 负责组织不同类别的 GameObjectPool。
/// </summary>
public sealed class GameObjectPool
{
    private readonly GameObject prefab;
    private readonly Transform storageRoot;
    private readonly int maxSize;
    private readonly Stack<PooledObject> inactiveObjects;
    private readonly Action<GameObject> onTaken;
    private readonly Action<GameObject> onReturned;

    public GameObjectPool(GameObject prefab,Transform storageRoot,int initialSize,int maxSize,
        Action<GameObject> onTaken = null,Action<GameObject> onReturned = null)
    {
        this.prefab = prefab;
        this.storageRoot = storageRoot;
        this.maxSize = Mathf.Max(1, maxSize);
        this.onTaken = onTaken;
        this.onReturned = onReturned;

        inactiveObjects = new Stack<PooledObject>(Mathf.Max(0, initialSize));

        int prewarmCount = Mathf.Min(Mathf.Max(0, initialSize), this.maxSize);

        for (int i = 0; i < prewarmCount; i++)
        {
            PooledObject pooledObject = CreateInstance();
            pooledObject.MarkReturned();
            inactiveObjects.Push(pooledObject);
        }
    }

    public GameObject Get(Vector3 worldPosition,Quaternion worldRotation,Transform activeParent = null)
    {
        PooledObject pooledObject = inactiveObjects.Count > 0
            ? inactiveObjects.Pop()
            : CreateInstance();

        pooledObject.MarkTaken();

        Transform instanceTransform = pooledObject.transform;
        Transform targetParent = activeParent != null
            ? activeParent
            : storageRoot;

        instanceTransform.SetParent(targetParent, true);
        instanceTransform.SetPositionAndRotation(worldPosition, worldRotation);

        pooledObject.gameObject.SetActive(true);
        onTaken?.Invoke(pooledObject.gameObject);

        return pooledObject.gameObject;
    }

    internal void Release(PooledObject pooledObject)
    {
        if (pooledObject == null || pooledObject.IsInPool)
        {
            return;
        }

        GameObject instance = pooledObject.gameObject;

        onReturned?.Invoke(instance);
        pooledObject.MarkReturned();

        instance.SetActive(false);
        pooledObject.transform.SetParent(storageRoot, false);

        if (inactiveObjects.Count >= maxSize)
        {
            UnityEngine.Object.Destroy(instance);
            return;
        }

        inactiveObjects.Push(pooledObject);
    }

    private PooledObject CreateInstance()
    {
        GameObject instance = UnityEngine.Object.Instantiate(prefab, storageRoot);
        instance.name = prefab.name;
        instance.SetActive(false);

        PooledObject pooledObject = instance.GetComponent<PooledObject>();

        if (pooledObject == null)
        {
            pooledObject = instance.AddComponent<PooledObject>();
        }

        pooledObject.Bind(this);
        onReturned?.Invoke(instance);

        return pooledObject;
    }
}
