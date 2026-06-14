using System.ComponentModel;
using System.IO;
using System.Text.Json.Serialization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using SteamEyaWinUI.Localization;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Models;

// partial：实例会作为 ListView ItemsSource 跨越 WinRT ABI，需要 CsWinRT 源生成 vtable（AOT）。
public sealed partial class SteamAccountHistoryItem : INotifyPropertyChanged
{
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
    public string AccountTitle => string.IsNullOrWhiteSpace(AccountName) ? Loc.T("Account_Title_Unnamed") : AccountName;

    [JsonIgnore]
    public string PersonaDisplayName => string.IsNullOrWhiteSpace(PersonaName) ? Loc.T("Account_Persona_NotSynced") : PersonaName;

    [JsonIgnore]
    public string SteamIdDisplay => string.IsNullOrWhiteSpace(SteamId) ? Loc.T("Account_Steam64_Unresolved") : SteamId;

    [JsonIgnore]
    public string LastLoginText => FormatHelper.FormatDateTime(LastLoginAt);

    [JsonIgnore]
    public string LastLoginShortText => LastLoginAt == default
        ? Loc.T("Account_LastLogin_Unknown")
        : LastLoginAt.LocalDateTime.ToString("MM-dd HH:mm");

    [JsonIgnore]
    public string LastLoginCaptionText => Loc.Tf("Account_LastLogin_Caption_Format", LastLoginShortText);

    [JsonIgnore]
    public string TokenExpiresText => TokenExpiresAt.HasValue
        ? FormatHelper.FormatDateTime(TokenExpiresAt.Value)
        : Loc.T("Account_TokenExpires_Unresolved");

    [JsonIgnore]
    public string CompetitiveScoreText
    {
        get
        {
            if (PremierScore.HasValue)
            {
                return PremierWins.HasValue
                    ? Loc.Tf("Account_Score_WithWins_Format", PremierScore.Value, PremierWins.Value)
                    : string.Format("{0:N0}", PremierScore.Value);
            }

            return string.IsNullOrWhiteSpace(CompetitiveScore) ? Loc.T("Account_Score_Pending") : CompetitiveScore;
        }
    }

    // GcVacBanned 是 bool?，转成 FormatHelper 的 int? 约定（null 未知 / 0 无 / 非 0 有标记）。
    private int? GcVacBannedAsInt => GcVacBanned.HasValue ? (GcVacBanned.Value ? 1 : 0) : null;

    [JsonIgnore]
    public string CooldownText => FormatHelper.FormatCooldownText(CooldownSeconds, CooldownReason, Loc.T("Account_Pending"));

    [JsonIgnore]
    public string CooldownSummaryText => Loc.Tf("Account_Cooldown_Summary_Format", CooldownText);

    [JsonIgnore]
    public string GcVacText => FormatHelper.FormatGcVacText(GcVacBannedAsInt, Loc.T("Account_Pending"));

    [JsonIgnore]
    public string CooldownStatusText =>
        FormatHelper.FormatCooldownStatusText(CooldownSeconds, CooldownReason, GcVacBannedAsInt, Loc.T("Account_Pending"), Loc.T("Account_Pending"));

    [JsonIgnore]
    public string CsPlayerLevelText => FormatHelper.FormatPlayerLevelText(CsPlayerLevel, Loc.T("Account_Pending"));

    [JsonIgnore]
    public string InCsMatchText => InCsMatch.HasValue
        ? (InCsMatch.Value ? Loc.T("Account_InMatch_Maybe") : Loc.T("Account_InMatch_None"))
        : Loc.T("Account_Pending");

    [JsonIgnore]
    public string AccountStatusText
    {
        get
        {
            var status = string.IsNullOrWhiteSpace(AccountStatus) ? Loc.T("Account_Pending") : AccountStatus;
            var updatedAt = CsStatusUpdatedAt ?? PremierScoreUpdatedAt;
            if (!updatedAt.HasValue)
            {
                return status;
            }

            return Loc.Tf("Account_Status_WithTime_Format", status, FormatHelper.FormatDateTime(updatedAt.Value));
        }
    }

    [JsonIgnore]
    public string JwtAvailabilityText
    {
        get
        {
            var status = JwtAvailable.HasValue
                ? (JwtAvailable.Value ? Loc.T("Account_Jwt_Valid") : Loc.T("Account_Jwt_Invalid"))
                : JwtStatus;

            if (string.IsNullOrWhiteSpace(status))
            {
                return Loc.T("Account_Pending");
            }

            return JwtValidatedAt.HasValue
                ? Loc.Tf("Account_Status_WithTime_Format", status, FormatHelper.FormatDateTime(JwtValidatedAt.Value))
                : status;
        }
    }

    // 进程级头像缓存：列表重建会换新实例，仅靠实例字段无法跨重建复用，必须用静态字典才真正止血。
    // 键 = 完整路径 + 最后写入时间，头像更新（重新下载覆盖同名文件）后键变化自动失效。
    // 仅 UI 线程访问（BitmapImage 也只能在 UI 线程使用），普通 Dictionary 即可。
    private static readonly Dictionary<string, BitmapImage> AvatarCache = new(StringComparer.OrdinalIgnoreCase);

    // PersonPicture 最大显示 92px（历史详情），按 2 倍留 DPI 余量解码。
    private const int AvatarDecodePixelWidth = 184;

    private BitmapImage? _avatarImage;

    [JsonIgnore]
    public BitmapImage? AvatarImage
    {
        get
        {
            if (_avatarImage is not null)
            {
                return _avatarImage;
            }

            _avatarImage = LoadAvatarImage();
            return _avatarImage;
        }
    }

    private BitmapImage? LoadAvatarImage()
    {
        var localPath = AvatarPath;
        if (!string.IsNullOrWhiteSpace(localPath) && File.Exists(localPath))
        {
            try
            {
                var cacheKey = $"{localPath}|{File.GetLastWriteTimeUtc(localPath):O}";
                if (AvatarCache.TryGetValue(cacheKey, out var cached))
                {
                    return cached;
                }

                // 从字节解码而非 new BitmapImage(Uri)：后者会长期持有文件句柄，导致删除账号时头像删不掉。
                var bytes = File.ReadAllBytes(localPath);
                var bitmap = new BitmapImage { DecodePixelWidth = AvatarDecodePixelWidth };
                using (var stream = new MemoryStream(bytes))
                {
                    // SetSource 同步消费完流后才返回，故 using 释放时机安全。
                    bitmap.SetSource(stream.AsRandomAccessStream());
                }

                AvatarCache[cacheKey] = bitmap;
                return bitmap;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Warn($"加载头像失败：{localPath}，{ex.Message}");
                // 落到下面的 URL 回退或返回 null（默认头像），与原行为一致。
            }
        }

        if (!string.IsNullOrWhiteSpace(AvatarUrl) &&
            Uri.TryCreate(AvatarUrl, UriKind.Absolute, out var avatarUri))
        {
            return new BitmapImage(avatarUri);
        }

        return null;
    }

    // ---------- 列表多选 / 悬停的瞬时 UI 状态（不持久化，仅驱动卡片视觉，列表重建后由页面重新套用） ----------

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _isSelected;

    /// <summary>是否被勾选进批量选择集。</summary>
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            RaiseSelectionVisuals();
        }
    }

    private bool _isPointerOver;

    /// <summary>鼠标是否悬停在卡片上（用于悬停时才显示勾选框）。</summary>
    [JsonIgnore]
    public bool IsPointerOver
    {
        get => _isPointerOver;
        set
        {
            if (_isPointerOver == value)
            {
                return;
            }

            _isPointerOver = value;
            RaiseSelectionVisuals();
        }
    }

    /// <summary>选中时显示：整卡黑框 + 左上角实心对勾。</summary>
    [JsonIgnore]
    public Visibility SelectionRingVisibility => _isSelected ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>左上角勾选指示器：悬停或已选时出现。</summary>
    [JsonIgnore]
    public Visibility CheckIndicatorVisibility =>
        _isSelected || _isPointerOver ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>未选中时（指示器可见即仅悬停场景）显示空心圈。</summary>
    [JsonIgnore]
    public Visibility EmptyCheckCircleVisibility => _isSelected ? Visibility.Collapsed : Visibility.Visible;

    private void RaiseSelectionVisuals()
    {
        var handler = PropertyChanged;
        if (handler is null)
        {
            return;
        }

        handler(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        handler(this, new PropertyChangedEventArgs(nameof(SelectionRingVisibility)));
        handler(this, new PropertyChangedEventArgs(nameof(CheckIndicatorVisibility)));
        handler(this, new PropertyChangedEventArgs(nameof(EmptyCheckCircleVisibility)));
    }
}
