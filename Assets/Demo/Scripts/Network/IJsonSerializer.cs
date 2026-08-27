using System;
using UnityEngine;

public interface IJsonSerializer
{
    string Serialize<T>(T value);
    T Deserialize<T>(string json);
}

public sealed class UnityJsonSerializer : IJsonSerializer
{
    public string Serialize<T>(T value)
    {
        try
        {
            return JsonUtility.ToJson(value);
        }
        catch (Exception exception)
        {
            throw new NetworkException(NetworkErrorKind.Serialization, "JSON 序列化失败。", innerException: exception);
        }
    }

    public T Deserialize<T>(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new NetworkException(NetworkErrorKind.Serialization, "服务器返回了空响应。");
        }

        try
        {
            T value = JsonUtility.FromJson<T>(json);

            if (ReferenceEquals(value, null))
            {
                throw new InvalidOperationException("反序列化结果为空。");
            }

            return value;
        }
        catch (NetworkException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new NetworkException(NetworkErrorKind.Serialization, "JSON 反序列化失败。", responseText: json,
                innerException: exception);
        }
    }
}
