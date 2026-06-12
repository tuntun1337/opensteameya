using System.Text.Json.Serialization;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SteamEyaWinUI.Models;

// partial：实例会作为 ListView ItemsSource 跨越 WinRT ABI，需要 CsWinRT 源生成 vtable（AOT）。
public sealed partial class SteamAccountHistoryItem
{
    private const int MinimumPremierLevel = 10;

    public string AccountName { get; set; } = "";

    public string SteamId { get; set; } = "";

    public string EyaToken { get; set; } = "";

    public string? PersonaName { get; set; }

    public string? AvatarUrl { get; set; }

    public string? AvatarPath { get; set; }

    public DateTimeOffset LastLoginAt { get; set; }

    public DateTimeOffset? TokenExpiresAt { get; set; }

    public string? CompetitiveScore { get; set; }

    public string? AccountStatus { get; set; }

    public bool? JwtAvailable { get; set; }

    public string? JwtStatus { get; set; }

    public DateTimeOffset? JwtValidatedAt { get; set; }

    public int? PremierScore { get; set; }

    public int? PremierWins { get; set; }

    public DateTimeOffset? PremierScoreUpdatedAt { get; set; }

    public uint? CooldownSeconds { get; set; }

    public uint? CooldownReason { get; set; }

    public bool? GcVacBanned { get; set; }

    public int? CsPlayerLevel { get; set; }

    public bool? InCsMatch { get; set; }

    public DateTimeOffset? CsStatusUpdatedAt { get; set; }

    [JsonIgnore]
    public string AccountTitle => string.IsNullOrWhiteSpace(AccountName) ? "未命名账号" : AccountName;

    [JsonIgnore]
    public string PersonaDisplayName => string.IsNullOrWhiteSpace(PersonaName) ? "Steam 资料未同步" : PersonaName;

    [JsonIgnore]
    public string SteamIdDisplay => string.IsNullOrWhiteSpace(SteamId) ? "Steam64 未解析" : SteamId;

    [JsonIgnore]
    public string LastLoginText => FormatDateTime(LastLoginAt);

    [JsonIgnore]
    public string LastLoginShortText => LastLoginAt == default
        ? "未知"
        : LastLoginAt.LocalDateTime.ToString("MM-dd HH:mm");

    [JsonIgnore]
    public string TokenExpiresText => TokenExpiresAt.HasValue
        ? FormatDateTime(TokenExpiresAt.Value)
        : "未解析";

    [JsonIgnore]
    public string CompetitiveScoreText
    {
        get
        {
            if (PremierScore.HasValue)
            {
                return PremierWins.HasValue
                    ? $"{PremierScore.Value:N0}（胜场 {PremierWins.Value}）"
                    : $"{PremierScore.Value:N0}";
            }

            return string.IsNullOrWhiteSpace(CompetitiveScore) ? "待查询" : CompetitiveScore;
        }
    }

    [JsonIgnore]
    public string CooldownText
    {
        get
        {
            if (!CooldownSeconds.HasValue)
            {
                return "待查询";
            }

            if (CooldownSeconds.Value == 0)
            {
                return "无";
            }

            var reason = CooldownReason.HasValue ? $"（原因 {CooldownReason.Value}）" : "";
            return $"{FormatDuration(CooldownSeconds.Value)}{reason}";
        }
    }

    [JsonIgnore]
    public string CooldownSummaryText => $"冷却：{CooldownText}";

    [JsonIgnore]
    public string GcVacText => GcVacBanned.HasValue
        ? (GcVacBanned.Value ? "有标记" : "无")
        : "待查询";

    [JsonIgnore]
    public string CooldownStatusText => $"冷却：{CooldownText}；GC VAC：{GcVacText}";

    [JsonIgnore]
    public string CsPlayerLevelText
    {
        get
        {
            if (!CsPlayerLevel.HasValue)
            {
                return "待查询";
            }

            var status = CsPlayerLevel.Value >= MinimumPremierLevel
                ? "可打优先"
                : $"未达 {MinimumPremierLevel} 级";

            return $"{CsPlayerLevel.Value} 级（{status}）";
        }
    }

    [JsonIgnore]
    public string InCsMatchText => InCsMatch.HasValue
        ? (InCsMatch.Value ? "可能在对局中" : "未发现对局")
        : "待查询";

    [JsonIgnore]
    public string AccountStatusText
    {
        get
        {
            var status = string.IsNullOrWhiteSpace(AccountStatus) ? "待查询" : AccountStatus;
            var updatedAt = CsStatusUpdatedAt ?? PremierScoreUpdatedAt;
            if (!updatedAt.HasValue)
            {
                return status;
            }

            return $"{status}，{FormatDateTime(updatedAt.Value)}";
        }
    }

    [JsonIgnore]
    public string JwtAvailabilityText
    {
        get
        {
            var status = JwtAvailable.HasValue
                ? (JwtAvailable.Value ? "有效" : "无效")
                : JwtStatus;

            if (string.IsNullOrWhiteSpace(status))
            {
                return "待查询";
            }

            return JwtValidatedAt.HasValue
                ? $"{status}，{FormatDateTime(JwtValidatedAt.Value)}"
                : status;
        }
    }

    [JsonIgnore]
    public BitmapImage? AvatarImage
    {
        get
        {
            var localPath = AvatarPath;
            if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
            {
                return new BitmapImage(new Uri(localPath, UriKind.Absolute));
            }

            if (!string.IsNullOrWhiteSpace(AvatarUrl) &&
                Uri.TryCreate(AvatarUrl, UriKind.Absolute, out var avatarUri))
            {
                return new BitmapImage(avatarUri);
            }

            return null;
        }
    }

    private static string FormatDateTime(DateTimeOffset value)
    {
        return value == default
            ? "未知时间"
            : value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private static string FormatDuration(uint seconds)
    {
        var days = seconds / 86400;
        var hours = seconds % 86400 / 3600;
        var minutes = seconds % 3600 / 60;
        var parts = new List<string>();

        if (days > 0)
        {
            parts.Add($"{days}天");
        }

        if (hours > 0)
        {
            parts.Add($"{hours}小时");
        }

        if (minutes > 0)
        {
            parts.Add($"{minutes}分");
        }

        return parts.Count > 0 ? string.Join("", parts) : $"{seconds}秒";
    }
}
