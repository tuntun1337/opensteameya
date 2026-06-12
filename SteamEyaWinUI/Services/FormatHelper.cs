using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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
        return $"{Math.Floor(remaining.TotalDays)} 天 {remaining.Hours} 小时 {remaining.Minutes} 分钟";
    }

    public static string FormatDateTime(DateTimeOffset value)
    {
        return value == default
            ? "未知时间"
            : value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public static string FormatFileSize(long? bytes)
    {
        if (!bytes.HasValue)
        {
            return "未知大小";
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
