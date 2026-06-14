using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteamEyaWinUI.Models;

/// <summary>
/// 一种界面语言：对应 Languages\&lt;code&gt;.json 一份文件。社区贡献语言只需新增一个这样的 JSON。
/// </summary>
internal sealed class LanguagePack
{
    /// <summary>语言代码（如 zh-Hans / en / zh-Hant / ja）。同时是文件名与去重键。</summary>
    public string Code { get; set; } = "";

    /// <summary>语言自称名（如 English / 简体中文），显示在设置页语言列表，始终以该语言自身书写。</summary>
    public string Name { get; set; } = "";

    /// <summary>缺键时回退到哪个语言代码（默认全局回退到 zh-Hans）。</summary>
    public string? Fallback { get; set; }

    /// <summary>键 → 译文。</summary>
    public Dictionary<string, string> Strings { get; set; } = new(StringComparer.Ordinal);
}

// 反射式 System.Text.Json 已全局禁用，语言文件走 source generator 解析（AOT 安全）。
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LanguagePack))]
internal sealed partial class LanguagePackJsonContext : JsonSerializerContext;
