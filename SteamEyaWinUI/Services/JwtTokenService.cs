using System.Text;
using System.Text.Json;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

public sealed class JwtTokenService
{
    public JwtValidationResult Validate(string refreshToken)
    {
        var info = Inspect(refreshToken);
        if (!info.IsValid)
        {
            throw new InvalidOperationException(info.Status);
        }

        return new JwtValidationResult(
            info.SteamId ?? throw new InvalidOperationException("EYA 令牌缺少 SteamID。"),
            info.ExpiresAt ?? throw new InvalidOperationException("EYA 令牌缺少过期时间。"));
    }

    public JwtTokenInfo Inspect(string refreshToken)
    {
        var parts = refreshToken.Split('.');
        if (parts.Length != 3)
        {
            return Invalid("EYA 令牌格式不正确。");
        }

        JsonDocument payload;
        try
        {
            payload = JsonDocument.Parse(Base64UrlDecode(parts[1]));
        }
        catch (Exception)
        {
            return Invalid("EYA 令牌无法解析。");
        }

        using (payload)
        {
        var root = payload.RootElement;

        if (!root.TryGetProperty("iss", out var issuer) ||
            issuer.GetString() != "steam")
        {
            return Invalid("EYA 令牌不是 Steam 令牌。");
        }

        if (!HasClientAudience(root))
        {
            return Invalid("EYA 令牌缺少 client 授权。", GetSteamId(root), GetExpiresAt(root));
        }

        if (!root.TryGetProperty("sub", out var subject))
        {
            return Invalid("EYA 令牌缺少 SteamID。", expiresAt: GetExpiresAt(root));
        }

        if (!root.TryGetProperty("exp", out var exp))
        {
            return Invalid("EYA 令牌缺少过期时间。", GetSteamId(root));
        }

        var steamId = subject.GetString();
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return Invalid("EYA 令牌中的 SteamID 为空。", expiresAt: GetExpiresAt(root));
        }

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64());
        var now = DateTimeOffset.Now;
        var remaining = expiresAt - now;
        if (expiresAt <= now)
        {
            return new JwtTokenInfo(steamId, expiresAt, false, "EYA 令牌已过期。", remaining);
        }

        if (root.TryGetProperty("nbf", out var notBeforeElement))
        {
            var notBefore = DateTimeOffset.FromUnixTimeSeconds(notBeforeElement.GetInt64());
            if (notBefore > now)
            {
                return new JwtTokenInfo(steamId, expiresAt, false, "EYA 令牌尚未生效。", remaining);
            }
        }

        return new JwtTokenInfo(steamId, expiresAt, true, "EYA 令牌有效。", remaining);
        }
    }

    private static bool HasClientAudience(JsonElement root)
    {
        if (!root.TryGetProperty("aud", out var audience))
        {
            return false;
        }

        if (audience.ValueKind == JsonValueKind.String)
        {
            return audience.GetString() == "client";
        }

        if (audience.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var value in audience.EnumerateArray())
        {
            if (value.GetString() == "client")
            {
                return true;
            }
        }

        return false;
    }

    private static string Base64UrlDecode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var padding = base64.Length % 4;
        if (padding > 0)
        {
            base64 += new string('=', 4 - padding);
        }

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static JwtTokenInfo Invalid(
        string status,
        string? steamId = null,
        DateTimeOffset? expiresAt = null)
    {
        var remaining = expiresAt.HasValue ? expiresAt.Value - DateTimeOffset.Now : (TimeSpan?)null;
        return new JwtTokenInfo(steamId, expiresAt, false, status, remaining);
    }

    private static string? GetSteamId(JsonElement root)
    {
        return root.TryGetProperty("sub", out var subject)
            ? subject.GetString()
            : null;
    }

    private static DateTimeOffset? GetExpiresAt(JsonElement root)
    {
        return root.TryGetProperty("exp", out var exp) && exp.TryGetInt64(out var seconds)
            ? DateTimeOffset.FromUnixTimeSeconds(seconds)
            : null;
    }
}

