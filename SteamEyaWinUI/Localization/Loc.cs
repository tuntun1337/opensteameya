using System.Globalization;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Localization;

/// <summary>
/// 代码层本地化门面。刻意不依赖 .resw / PRI——无打包 WinUI + Native AOT 下 PRI 资源索引易碎
/// （csproj 里已有 IncludeWinUIPriInPublish 补丁）。界面字符串来自 Languages\*.json 语言包
/// （见 <see cref="LanguageCatalog"/>），方便社区贡献翻译：新增一个 &lt;code&gt;.json 即多一种语言。
///
/// 三种用法：
///  · XAML 静态文本：{x:Bind Strings.Get('Key'), Mode=OneWay}——走 <see cref="LocalizedStrings"/>，宿主页在
///    <see cref="LanguageChanged"/> 时对 Strings 触发 PropertyChanged 即可实时切换。
///  · DataTemplate 内部文本：{loc:Localize Key=...}——见 <see cref="LocalizeExtension"/>（加载期取值，随列表重建刷新）。
///  · 代码层命令式文案：<see cref="T"/> / <see cref="Tf"/>。
/// </summary>
internal static class Loc
{
    private const string DefaultFallbackCode = "zh-Hans";

    private static SettingsService? _settings;
    private static IReadOnlyList<LanguagePack> _packs = Array.Empty<LanguagePack>();
    private static LanguagePack _current = EmptyPack();
    private static LanguagePack _fallback = EmptyPack();

    /// <summary>当前语言代码（如 zh-Hans / en / zh-Hant）。</summary>
    public static string CurrentCode => _current.Code;

    /// <summary>可选语言（按显示顺序），用于设置页的语言列表。</summary>
    public static IReadOnlyList<LanguagePack> AvailablePacks => _packs;

    /// <summary>XAML 绑定入口单例：{x:Bind Strings.Get('Key'), Mode=OneWay}。</summary>
    public static LocalizedStrings Strings { get; } = new();

    /// <summary>语言变更后触发（UI 线程）：页面据此重跑命令式渲染并对 Strings 触发 PropertyChanged 以实时切换。</summary>
    public static event Action? LanguageChanged;

    /// <summary>启动时调用一次：加载语言包，按设置中的语言（无则按系统语言推断）应用。</summary>
    public static void Initialize(SettingsService settings)
    {
        _settings = settings;
        _packs = LanguageCatalog.Load();

        var saved = settings.Load().Language;
        var code = !string.IsNullOrWhiteSpace(saved) && HasPack(saved)
            ? saved!
            : DetectSystemLanguage();
        ApplyLanguage(code);
    }

    /// <summary>切换语言：刷新所有 x:Bind 文本、触发 <see cref="LanguageChanged"/>，并持久化选择。</summary>
    public static void SetLanguage(string code, bool persist = true)
    {
        if (string.IsNullOrWhiteSpace(code) || code == CurrentCode || !HasPack(code))
        {
            return;
        }

        ApplyLanguage(code);

        if (persist && _settings is not null)
        {
            var settings = _settings.Load();
            settings.Language = code;
            _settings.Save(settings);
        }
    }

    public static string T(string key)
    {
        if (_current.Strings.TryGetValue(key, out var value))
        {
            return value;
        }

        return _fallback.Strings.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string Tf(string key, params object?[] args) => string.Format(T(key), args);

    private static void ApplyLanguage(string code)
    {
        _current = FindPack(code) ?? FindPack(DefaultFallbackCode) ?? (_packs.Count > 0 ? _packs[0] : EmptyPack());

        // 回退包：优先当前语言声明的 fallback，否则全局 zh-Hans，再否则当前包自身。
        _fallback = FindPack(_current.Fallback ?? DefaultFallbackCode)
            ?? FindPack(DefaultFallbackCode)
            ?? _current;

        Strings.RaiseAllChanged();
        LanguageChanged?.Invoke();
    }

    private static bool HasPack(string code) => FindPack(code) is not null;

    private static LanguagePack? FindPack(string? code) => code is null
        ? null
        : _packs.FirstOrDefault(pack => string.Equals(pack.Code, code, StringComparison.OrdinalIgnoreCase));

    private static LanguagePack EmptyPack() => new() { Code = DefaultFallbackCode, Name = DefaultFallbackCode };

    /// <summary>按系统界面语言挑一个已加载的语言代码；挑不到则退到首个可用包。</summary>
    private static string DetectSystemLanguage()
    {
        try
        {
            var name = CultureInfo.CurrentUICulture.Name; // 形如 zh-CN / zh-TW / en-US

            if (name.StartsWith("en", StringComparison.OrdinalIgnoreCase) && HasPack("en"))
            {
                return "en";
            }

            if (name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            {
                var isTraditional =
                    name.Contains("Hant", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("TW", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("HK", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("MO", StringComparison.OrdinalIgnoreCase);
                if (isTraditional && HasPack("zh-Hant"))
                {
                    return "zh-Hant";
                }

                if (HasPack("zh-Hans"))
                {
                    return "zh-Hans";
                }
            }

            // 完整文化名或两字母前缀直接命中某个包（如 ja-JP → ja）。
            if (HasPack(name))
            {
                return name;
            }

            var twoLetter = name.Length >= 2 ? name[..2] : name;
            if (HasPack(twoLetter))
            {
                return twoLetter;
            }

            // 非中英文系统：有英文优先英文。
            if (HasPack("en"))
            {
                return "en";
            }
        }
        catch
        {
            // 推断失败按下面的兜底处理。
        }

        return HasPack(DefaultFallbackCode) ? DefaultFallbackCode : (_packs.Count > 0 ? _packs[0].Code : DefaultFallbackCode);
    }
}
