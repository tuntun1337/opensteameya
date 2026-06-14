using SteamEyaWinUI.Localization;

namespace SteamEyaWinUI.Models;

// partial：实例会作为导入对话框 ListView 的 ItemsSource 跨越 WinRT ABI，需要 CsWinRT 源生成 vtable（AOT）。
public sealed partial class AccountImportEntry
{
    public string AccountName { get; set; } = "";

    public string EyaToken { get; set; } = "";

    public string SteamId { get; set; } = "";

    public DateTimeOffset? TokenExpiresAt { get; set; }

    public bool TokenIsValid { get; set; }

    public string TokenStatus { get; set; } = "";

    public bool AlreadyExists { get; set; }

    public string SummaryText => AlreadyExists
        ? Loc.Tf("Import_Summary_Exists_Format", SteamId)
        : Loc.Tf("Import_Summary_New_Format", SteamId);

    public string TokenSummaryText => TokenIsValid && TokenExpiresAt.HasValue
        ? Loc.Tf("Import_Token_ValidUntil_Format", string.Format("{0:yyyy-MM-dd HH:mm}", TokenExpiresAt.Value.LocalDateTime))
        : Loc.Tf("Import_Token_Status_Format", TokenStatus);
}
