namespace SteamEyaWinUI.Models;

public sealed record CsPremierScoreResult(
    string SteamId,
    uint AccountId,
    CsRankingInfo? PremierRanking,
    IReadOnlyList<CsRankingInfo> Rankings,
    uint PenaltySeconds,
    uint PenaltyReason,
    int VacBanned,
    int? PlayerLevel,
    bool InMatch)
{
    private const int MinimumPremierLevel = 10;

    public bool HasPremierScore => PremierRanking is not null && PremierRanking.RankId > 0;

    public bool HasCooldown => PenaltySeconds > 0;

    public bool IsGcVacBanned => VacBanned != 0;

    public string DisplayText => HasPremierScore
        ? $"{PremierRanking!.RankId:N0}（胜场 {PremierRanking.Wins}）"
        : "暂无优先分";

    public string CooldownText => HasCooldown
        ? $"{FormatDuration(PenaltySeconds)}（原因 {PenaltyReason}）"
        : "无";

    public string GcVacText => IsGcVacBanned ? "有标记" : "无";

    public string CooldownStatusText => $"冷却：{CooldownText}；GC VAC：{GcVacText}";

    public string PlayerLevelText
    {
        get
        {
            if (!PlayerLevel.HasValue)
            {
                return "未读取";
            }

            var status = PlayerLevel.Value >= MinimumPremierLevel
                ? "可打优先"
                : $"未达 {MinimumPremierLevel} 级";

            return $"{PlayerLevel.Value} 级（{status}）";
        }
    }

    public string StatusText
    {
        get
        {
            var restrictions = new List<string>();
            if (IsGcVacBanned)
            {
                restrictions.Add("GC 标记 VAC");
            }

            if (HasCooldown)
            {
                var kind = PenaltySeconds > 365U * 86400U
                    ? "长期/永久竞技封禁"
                    : "竞技冷却";
                restrictions.Add($"{kind}：{CooldownText}");
            }

            return restrictions.Count == 0
                ? "未发现 CS2 限制"
                : string.Join("；", restrictions);
        }
    }

    private static string FormatDuration(uint seconds)
    {
        if (seconds == 0)
        {
            return "无";
        }

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

public sealed record CsRankingInfo(
    uint RankTypeId,
    uint RankId,
    uint Wins,
    uint? MapId);
