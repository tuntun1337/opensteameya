using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

internal sealed class SteamTokenOnlineValidationService
{
    private readonly JwtTokenService _jwtTokenService = new();

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<SteamTokenOnlineValidationResult> ValidateAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var tokenInfo = _jwtTokenService.Inspect(refreshToken);
        if (!tokenInfo.IsValid)
        {
            return new SteamTokenOnlineValidationResult(false, tokenInfo.Status);
        }

        if (string.IsNullOrWhiteSpace(tokenInfo.SteamId))
        {
            return new SteamTokenOnlineValidationResult(false, "EYA 令牌缺少 SteamID。");
        }

        await using var cmClient = new SteamCmClient(HttpClient);
        try
        {
            await cmClient.ConnectAndLogOnAsync(refreshToken, tokenInfo.SteamId, cancellationToken);
            return new SteamTokenOnlineValidationResult(true, "Steam 已接受该令牌。");
        }
        catch (SteamCmException ex) when (ex.IsTokenFailure)
        {
            return new SteamTokenOnlineValidationResult(false, ex.Message);
        }
    }
}
