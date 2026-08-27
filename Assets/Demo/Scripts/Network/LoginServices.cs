using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public interface ILoginService
{
    Task<LoginResponse> LoginAsync(string account, string password, CancellationToken cancellationToken = default);
}

public interface IPlayerService
{
    Task<PlayerProfile> GetPlayerAsync(string playerId, string token, CancellationToken cancellationToken = default);
}

public sealed class LoginService : ILoginService
{
    private readonly string baseUrl;
    private readonly IHttpClient httpClient;
    private readonly IJsonSerializer jsonSerializer;
    private readonly int timeoutSeconds;
    private readonly int maximumAttempts;

    public LoginService(string baseUrl, IHttpClient httpClient, IJsonSerializer jsonSerializer, int timeoutSeconds,
        int maximumAttempts)
    {
        this.baseUrl = NormalizeBaseUrl(baseUrl);
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.jsonSerializer = jsonSerializer ?? throw new ArgumentNullException(nameof(jsonSerializer));
        this.timeoutSeconds = Math.Max(1, timeoutSeconds);
        this.maximumAttempts = Math.Max(1, maximumAttempts);
    }

    public async Task<LoginResponse> LoginAsync(string account, string password,
        CancellationToken cancellationToken = default)
    {
        LoginRequest body = new LoginRequest { account = account, password = password };
        HttpRequest request = new HttpRequest($"{baseUrl}/api/login", NetworkHttpMethod.Post,
            jsonSerializer.Serialize(body), timeoutSeconds, maximumAttempts);
        HttpResponse<LoginResponse> response = await httpClient.SendAsync<LoginResponse>(request, cancellationToken);

        if (response.Data == null || string.IsNullOrWhiteSpace(response.Data.token) ||
            string.IsNullOrWhiteSpace(response.Data.playerId))
        {
            throw new NetworkException(NetworkErrorKind.Serialization, "登录响应缺少 Token 或玩家 ID。",
                responseText: response.RawText);
        }

        return response.Data;
    }

    private static string NormalizeBaseUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("API Base URL 不能为空。", nameof(value));
        }

        return value.Trim().TrimEnd('/');
    }
}

public sealed class PlayerService : IPlayerService
{
    private readonly string baseUrl;
    private readonly IHttpClient httpClient;
    private readonly int timeoutSeconds;
    private readonly int maximumAttempts;

    public PlayerService(string baseUrl, IHttpClient httpClient, int timeoutSeconds, int maximumAttempts)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new ArgumentException("API Base URL 不能为空。", nameof(baseUrl));
        }

        this.baseUrl = baseUrl.Trim().TrimEnd('/');
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.timeoutSeconds = Math.Max(1, timeoutSeconds);
        this.maximumAttempts = Math.Max(1, maximumAttempts);
    }

    public async Task<PlayerProfile> GetPlayerAsync(string playerId, string token,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerId) || string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("玩家 ID 和 Token 不能为空。");
        }

        Dictionary<string, string> headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {token}"
        };

        string escapedPlayerId = Uri.EscapeDataString(playerId);
        HttpRequest request = new HttpRequest($"{baseUrl}/api/player/{escapedPlayerId}", NetworkHttpMethod.Get, null,
            timeoutSeconds, maximumAttempts, headers);
        HttpResponse<PlayerProfile> response = await httpClient.SendAsync<PlayerProfile>(request, cancellationToken);

        if (response.Data == null || string.IsNullOrWhiteSpace(response.Data.id))
        {
            throw new NetworkException(NetworkErrorKind.Serialization, "玩家数据响应缺少玩家 ID。",
                responseText: response.RawText);
        }

        return response.Data;
    }
}
