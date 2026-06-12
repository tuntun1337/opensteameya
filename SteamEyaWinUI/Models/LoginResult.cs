namespace SteamEyaWinUI.Models;

public sealed record LoginResult(string AccountName, string SteamId, DateTimeOffset ExpiresAt)
{
    public TimeSpan Remaining => ExpiresAt - DateTimeOffset.Now;
}
