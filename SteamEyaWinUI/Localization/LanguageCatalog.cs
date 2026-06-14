using System.IO;
using System.Reflection;
using System.Text.Json;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Localization;

/// <summary>
/// 发现并加载所有界面语言包。来源（后者覆盖前者同代码语言，便于本地改包）：
///  1. 内嵌资源（随程序集打包的 Languages\*.json，保底——发布偶尔漏拷文件时仍可用）；
///  2. 程序目录 Languages\*.json（随发布拷贝，贡献者改这里）；
///  3. %AppData%\SteamEYA\Languages\*.json（用户拖入新语言，无需重新编译）。
/// 新增一种语言 = 新增一个 &lt;code&gt;.json，应用启动即自动出现在设置里。
/// </summary>
internal static class LanguageCatalog
{
    private const string AppFolderName = "SteamEYA";
    private const string LanguagesFolderName = "Languages";

    // 显示与回退的首选顺序；其余语言按名称排在其后。
    private static readonly string[] PreferredOrder = { "zh-Hans", "en", "zh-Hant" };

    /// <summary>用户可拖入自定义语言包的目录（%AppData%\SteamEYA\Languages）。</summary>
    public static string UserLanguagesFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppFolderName,
        LanguagesFolderName);

    /// <summary>加载全部语言包，按显示顺序返回。至少应含内嵌的 zh-Hans。</summary>
    public static IReadOnlyList<LanguagePack> Load()
    {
        var byCode = new Dictionary<string, LanguagePack>(StringComparer.OrdinalIgnoreCase);

        LoadEmbedded(byCode);
        LoadDirectory(Path.Combine(AppContext.BaseDirectory, LanguagesFolderName), byCode);
        LoadDirectory(UserLanguagesFolder, byCode);

        return byCode.Values
            .OrderBy(pack =>
            {
                var index = Array.IndexOf(PreferredOrder, pack.Code);
                return index < 0 ? int.MaxValue : index;
            })
            .ThenBy(pack => pack.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void LoadEmbedded(Dictionary<string, LanguagePack> byCode)
    {
        var assembly = typeof(LanguageCatalog).Assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            // 资源名形如 SteamEyaWinUI.Languages.en.json。
            if (!name.Contains(".Languages.", StringComparison.Ordinal) ||
                !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream is null)
                {
                    continue;
                }

                var pack = JsonSerializer.Deserialize(stream, LanguagePackJsonContext.Default.LanguagePack);
                AddPack(byCode, pack);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"内嵌语言资源 {name} 解析失败，已跳过：{ex.Message}");
            }
        }
    }

    private static void LoadDirectory(string directory, Dictionary<string, LanguagePack> byCode)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var pack = JsonSerializer.Deserialize(json, LanguagePackJsonContext.Default.LanguagePack);
                AddPack(byCode, pack);
            }
            catch (Exception ex)
            {
                AppLog.Warn($"语言文件 {file} 解析失败，已跳过：{ex.Message}");
            }
        }
    }

    private static void AddPack(Dictionary<string, LanguagePack> byCode, LanguagePack? pack)
    {
        if (pack is null || string.IsNullOrWhiteSpace(pack.Code) || pack.Strings.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(pack.Name))
        {
            pack.Name = pack.Code;
        }

        // 后加载来源覆盖先前同代码包（用户目录 > 程序目录 > 内嵌）。
        byCode[pack.Code] = pack;
    }
}
