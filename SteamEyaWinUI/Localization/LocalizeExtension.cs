using Microsoft.UI.Xaml.Markup;

namespace SteamEyaWinUI.Localization;

/// <summary>
/// DataTemplate 内部静态文本的本地化标记扩展：{loc:Localize Key=History_Card_QuickLogin}。
/// DataTemplate 里的 x:Bind 根是数据项而非页面，够不到页面的 Strings，故这类文本改用本扩展。
/// 取值发生在元素实现化（加载）时；语言切换后已实现化的容器需等列表重建才刷新——对工具提示等可接受。
/// </summary>
public sealed partial class LocalizeExtension : MarkupExtension
{
    public string Key { get; set; } = "";

    protected override object ProvideValue() => Loc.T(Key);
}
