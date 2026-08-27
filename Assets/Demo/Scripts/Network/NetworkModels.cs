using System;
using System.Collections.Generic;

public enum NetworkErrorKind
{
    Connection,
    Timeout,
    Http,
    Serialization,
    Cancelled
}

public static class NetworkHttpMethod
{
    public const string Get = "GET";
    public const string Post = "POST";
}

public sealed class HttpRequest
{
    public string Url { get; }
    public string Method { get; }
    public string Body { get; }
    public int TimeoutSeconds { get; }
    public int MaximumAttempts { get; }
    public IReadOnlyDictionary<string, string> Headers { get; }

    public HttpRequest(string url, string method, string body, int timeoutSeconds, int maximumAttempts,
        IReadOnlyDictionary<string, string> headers = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("请求地址不能为空。", nameof(url));
        }

        Url = url;
        Method = string.IsNullOrWhiteSpace(method) ? NetworkHttpMethod.Get : method;
        Body = body;
        TimeoutSeconds = Math.Max(1, timeoutSeconds);
        MaximumAttempts = Math.Max(1, maximumAttempts);
        Headers = headers ?? new Dictionary<string, string>();
    }
}

public readonly struct HttpResponse<T>
{
    public long StatusCode { get; }
    public T Data { get; }
    public string RawText { get; }

    public HttpResponse(long statusCode, T data, string rawText)
    {
        StatusCode = statusCode;
        Data = data;
        RawText = rawText;
    }
}

public readonly struct HttpRetryInfo
{
    public string Url { get; }
    public int NextAttempt { get; }
    public int MaximumAttempts { get; }
    public float DelaySeconds { get; }
    public NetworkException Error { get; }

    public HttpRetryInfo(string url, int nextAttempt, int maximumAttempts, float delaySeconds, NetworkException error)
    {
        Url = url;
        NextAttempt = nextAttempt;
        MaximumAttempts = maximumAttempts;
        DelaySeconds = delaySeconds;
        Error = error;
    }
}

public sealed class NetworkException : Exception
{
    public NetworkErrorKind Kind { get; }
    public long StatusCode { get; }
    public string ResponseText { get; }
    public bool IsRetryable { get; }

    public NetworkException(NetworkErrorKind kind, string message, long statusCode = 0, string responseText = null,
        bool isRetryable = false, Exception innerException = null) : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
        ResponseText = responseText;
        IsRetryable = isRetryable;
    }

    public string GetUserMessage()
    {
        switch (Kind)
        {
            case NetworkErrorKind.Connection:
                return "无法连接服务器，请检查网络后重试。";
            case NetworkErrorKind.Timeout:
                return "请求超时，请稍后重试。";
            case NetworkErrorKind.Serialization:
                return "服务器返回的数据格式不正确。";
            case NetworkErrorKind.Cancelled:
                return "请求已取消。";
            case NetworkErrorKind.Http:
                if (StatusCode == 401)
                {
                    return "身份验证失败。";
                }

                if (StatusCode == 403)
                {
                    return "当前账号没有执行此操作的权限。";
                }

                if (StatusCode == 404)
                {
                    return "请求的服务器接口不存在。";
                }

                if (StatusCode == 429)
                {
                    return "请求过于频繁，请稍后重试。";
                }

                return StatusCode >= 500 ? "服务器暂时不可用，请稍后重试。" : $"请求失败（HTTP {StatusCode}）。";
            default:
                return "网络请求失败，请稍后重试。";
        }
    }
}
