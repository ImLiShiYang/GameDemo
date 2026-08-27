using System;
using System.Threading;
using System.Threading.Tasks;

public interface IHttpClient
{
    event Action<HttpRetryInfo> Retrying;

    Task<HttpResponse<TResponse>> SendAsync<TResponse>(HttpRequest request, CancellationToken cancellationToken = default);
}
