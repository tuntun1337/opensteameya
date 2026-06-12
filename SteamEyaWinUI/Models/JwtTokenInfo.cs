namespace SteamEyaWinUI.Models;

public sealed record JwtTokenInfo(
    string? SteamId,
    DateTimeOffset? ExpiresAt,
    bool IsValid,
    string Status,
    TimeSpan? Remaining);
