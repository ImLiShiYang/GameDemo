using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

public sealed class UnityWebRequestHttpClient : IHttpClient
{
    private const int InitialRetryDelayMilliseconds = 500;
    private readonly IJsonSerializer jsonSerializer;

    public event Action<HttpRetryInfo> Retrying;

    public UnityWebRequestHttpClient(IJsonSerializer jsonSerializer)
    {
        this.jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
    }

    public async Task<HttpResponse<TResponse>> SendAsync<TResponse>(HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        for (int attempt = 1; attempt <= request.MaximumAttempts; attempt++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw CreateCancelledException();
            }

            try
            {
                return await SendOnceAsync<TResponse>(request, cancellationToken);
            }
            catch (NetworkException exception) when (exception.IsRetryable && attempt < request.MaximumAttempts)
            {
                int delayMilliseconds = InitialRetryDelayMilliseconds * (1 << (attempt - 1));
                Retrying?.Invoke(new HttpRetryInfo(request.Url, attempt + 1, request.MaximumAttempts,
                    delayMilliseconds / 1000f, exception));

                try
                {
                    await Task.Delay(delayMilliseconds, cancellationToken);
                }
                catch (OperationCanceledException cancelledException)
                {
                    throw CreateCancelledException(cancelledException);
                }
            }
            catch (OperationCanceledException exception)
            {
                throw CreateCancelledException(exception);
            }
        }

        throw new InvalidOperationException("HTTP 重试流程异常结束。");
    }

    private async Task<HttpResponse<TResponse>> SendOnceAsync<TResponse>(HttpRequest request,
        CancellationToken cancellationToken)
    {
        using UnityWebRequest webRequest = CreateWebRequest(request);
        using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(webRequest.Abort);
        UnityWebRequestAsyncOperation operation = webRequest.SendWebRequest();

        while (!operation.isDone)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                webRequest.Abort();
                throw CreateCancelledException();
            }

            await Task.Yield();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            throw CreateCancelledException();
        }

        string responseText = webRequest.downloadHandler?.text;

        if (webRequest.result == UnityWebRequest.Result.Success)
        {
            TResponse data = jsonSerializer.Deserialize<TResponse>(responseText);
            return new HttpResponse<TResponse>(webRequest.responseCode, data, responseText);
        }

        throw CreateRequestException(webRequest, responseText);
    }

    private static UnityWebRequest CreateWebRequest(HttpRequest request)
    {
        UnityWebRequest webRequest = new UnityWebRequest(request.Url, request.Method)
        {
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = request.TimeoutSeconds,
            // Python 的轻量 Mock 服务不需要 Expect: 100-continue 握手。
            // 关闭后请求体会立即发送，避免客户端等待 100 Continue、服务端等待请求体造成互相阻塞。
            useHttpContinue = false
        };

        if (!string.IsNullOrEmpty(request.Body))
        {
            webRequest.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(request.Body));
            webRequest.SetRequestHeader("Content-Type", "application/json; charset=utf-8");
        }

        foreach (var header in request.Headers)
        {
            webRequest.SetRequestHeader(header.Key, header.Value);
        }

        return webRequest;
    }

    private static NetworkException CreateRequestException(UnityWebRequest request, string responseText)
    {
        if (request.result == UnityWebRequest.Result.ProtocolError)
        {
            long statusCode = request.responseCode;
            bool retryable = statusCode == 408 || statusCode == 429 || statusCode >= 500;
            return new NetworkException(NetworkErrorKind.Http, request.error ?? $"HTTP {statusCode}", statusCode,
                responseText, retryable);
        }

        bool timedOut = request.error?.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        request.error?.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;

        return timedOut
            ? new NetworkException(NetworkErrorKind.Timeout, $"{request.error ?? "请求超时。"} URL={request.url}",
                responseText: responseText, isRetryable: true)
            : new NetworkException(NetworkErrorKind.Connection,
                $"{request.error ?? "无法连接服务器。"} URL={request.url}",
                responseText: responseText, isRetryable: true);
    }

    private static NetworkException CreateCancelledException(Exception innerException = null)
    {
        return new NetworkException(NetworkErrorKind.Cancelled, "网络请求已取消。", innerException: innerException);
    }
}
