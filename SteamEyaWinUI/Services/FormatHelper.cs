using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamEyaWinUI.Localization;

namespace SteamEyaWinUI.Services;

internal static class FormatHelper
{
    /// <summary>修正令牌头部常见的大小写粘贴错误（EyAi → eyAi）。</summary>
    public static string NormalizeToken(string token)
    {
        return token.Replace(
            "EyAidHlwIjogIkpXVCIsICJhbGciOiAiRWREU0EiIH0",
            "eyAidHlwIjogIkpXVCIsICJhbGciOiAiRWREU0EiIH0",
            StringComparison.Ordinal);
    }

    public static string FormatRemaining(TimeSpan remaining)
    {
        return Loc.Tf("Format_Remaining_Format", Math.Floor(remaining.TotalDays), remaining.Hours, remaining.Minutes);
    }

    public static string FormatDateTime(DateTimeOffset value)
    {
        return value == default
            ? Loc.T("Format_DateTime_Unknown")
            : value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    /// <summary>优先分达到此等级才能打优先匹配。</summary>
    public const int MinimumPremierLevel = 10;

    public static string FormatDuration(uint seconds)
    {
        var days = seconds / 86400;
        var hours = seconds % 86400 / 3600;
        var minutes = seconds % 3600 / 60;
        var parts = new List<string>();

        if (days > 0)
        {
            parts.Add(Loc.Tf("Format_Duration_Days_Format", days));
        }

        if (hours > 0)
        {
            parts.Add(Loc.Tf("Format_Duration_Hours_Format", hours));
        }

        if (minutes > 0)
        {
            parts.Add(Loc.Tf("Format_Duration_Minutes_Format", minutes));
        }

        return parts.Count > 0 ? string.Join("", parts) : Loc.Tf("Format_Duration_Seconds_Format", seconds);
    }

    // 已知的 GC 冷却原因码映射为可读文案；未知码保留 "原因 N"；
    // reason 为 null（GC 未给原因码）返回空串，由 FormatCooldownText 据此省略原因括号，
    // 保持历史侧"无原因码则不显示原因"的旧观感（而非误导性的"原因 0"）。
    public static string DescribePenaltyReason(uint? reason) => reason switch
    {
        null => "",
        7 => Loc.T("Format_Penalty_Abandon"),
        22 => "vaclive",
        _ => Loc.Tf("Format_Penalty_Reason_Format", reason.Value)
    };

    /// <param name="seconds">冷却剩余秒数；null 表示 GC 未下发（未知）。</param>
    /// <param name="unknownText">seconds 为 null 时的文案（两处调用方措辞不同，参数化）。</param>
    public static string FormatCooldownText(uint? seconds, uint? reason, string unknownText)
    {
        if (seconds is null)
        {
            return unknownText;
        }

        if (seconds == 0)
        {
            return Loc.T("Format_Cooldown_None");
        }

        var duration = FormatDuration(seconds.Value);
        var description = DescribePenaltyReason(reason);
        return description.Length > 0 ? Loc.Tf("Format_Cooldown_WithReason_Format", duration, description) : duration;
    }

    /// <param name="vacBanned">GC VAC 标记：null 未知 / 0 无 / 其他 有标记。</param>
    /// <param name="unknownText">vacBanned 为 null 时的文案。</param>
    public static string FormatGcVacText(int? vacBanned, string unknownText) => vacBanned switch
    {
        null => unknownText,
        0 => Loc.T("Format_GcVac_None"),
        _ => Loc.T("Format_GcVac_Flagged")
    };

    public static string FormatCooldownStatusText(
        uint? seconds,
        uint? reason,
        int? vacBanned,
        string cooldownUnknownText,
        string vacUnknownText) =>
        Loc.Tf(
            "Format_CooldownStatus_Format",
            FormatCooldownText(seconds, reason, cooldownUnknownText),
            FormatGcVacText(vacBanned, vacUnknownText));

    /// <param name="level">CS 玩家等级；null 时返回 <paramref name="unknownText"/>。</param>
    public static string FormatPlayerLevelText(int? level, string unknownText)
    {
        if (!level.HasValue)
        {
            return unknownText;
        }

        var status = level.Value >= MinimumPremierLevel
            ? Loc.T("Format_PlayerLevel_Eligible")
            : Loc.Tf("Format_PlayerLevel_Below_Format", MinimumPremierLevel);

        return Loc.Tf("Format_PlayerLevel_Format", level.Value, status);
    }

    public static string FormatFileSize(long? bytes)
    {
        if (!bytes.HasValue)
        {
            return Loc.T("Format_FileSize_Unknown");
        }

        return bytes.Value >= 1024 * 1024
            ? $"{bytes.Value / 1024d / 1024d:F2} MB"
            : $"{bytes.Value / 1024d:F0} KB";
    }

    public static Brush GetStatusBrush(InfoBarSeverity severity)
    {
        var resourceKey = severity switch
        {
            InfoBarSeverity.Success => "SystemFillColorSuccessBrush",
            InfoBarSeverity.Error => "SystemFillColorCriticalBrush",
            _ => "TextFillColorSecondaryBrush"
        };

        if (Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}
