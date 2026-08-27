using System;

[Serializable]
public sealed class LoginRequest
{
    public string account;
    public string password;
}

[Serializable]
public sealed class LoginResponse
{
    public string token;
    public string playerId;
    public int expiresInSeconds;
}

[Serializable]
public sealed class PlayerProfile
{
    public string id;
    public string nickname;
    public int level;
    public int experience;
}

[Serializable]
public sealed class ApiErrorResponse
{
    public string code;
    public string message;
}

public static class GameSession
{
    public static string Token { get; private set; } = string.Empty;
    public static PlayerProfile Player { get; private set; }
    public static bool IsAuthenticated => !string.IsNullOrEmpty(Token) && Player != null;

    public static void Set(LoginResponse login, PlayerProfile player)
    {
        if (login == null || string.IsNullOrWhiteSpace(login.token))
        {
            throw new ArgumentException("登录响应中没有有效 Token。", nameof(login));
        }

        Player = player ?? throw new ArgumentNullException(nameof(player));
        Token = login.token;
    }

    public static void Clear()
    {
        Token = string.Empty;
        Player = null;
    }
}
