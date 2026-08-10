using UnityEngine;

/// <summary>
/// 由对象池自动挂到运行时实例上。
/// 业务脚本只需要调用 Release()，不需要知道自己属于哪个具体池。
/// </summary>
public sealed class PooledObject : MonoBehaviour
{
    private GameObjectPool ownerPool;

    internal int Version { get; private set; }
    internal bool IsInPool { get; private set; }

    internal void Bind(GameObjectPool pool)
    {
        ownerPool = pool;
    }

    internal void MarkTaken()
    {
        IsInPool = false;
        Version++;
    }

    internal void MarkReturned()
    {
        IsInPool = true;
    }

    public void Release()
    {
        if (ownerPool == null)
        {
            Debug.LogWarning($"{name} 没有关联对象池，将直接销毁。", this);
            Destroy(gameObject);
            return;
        }

        ownerPool.Release(this);
    }
}
