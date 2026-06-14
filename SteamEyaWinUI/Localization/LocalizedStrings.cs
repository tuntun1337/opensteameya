using System.ComponentModel;

namespace SteamEyaWinUI.Localization;

/// <summary>
/// XAML 绑定入口对象。XAML 写 {x:Bind Strings.Get('Key'), Mode=OneWay}，由编译期 x:Bind 生成函数绑定调用 <see cref="Get"/>。
/// 自身无语言状态——键查询转发到 <see cref="Loc.T"/>。
///
/// 实时切换机制：本对象是单例，标识不变，故不能靠自身的 PropertyChanged 让 x:Bind 重算；
/// 而是宿主页面在 <see cref="Loc.LanguageChanged"/> 时对其 “Strings” 属性触发 PropertyChanged，
/// x:Bind 重新读取 Strings → 重新调用 Get('Key') 取到新语言文本。<see cref="RaiseAllChanged"/> 仅为对称保留。
/// </summary>
internal sealed partial class LocalizedStrings : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>按键取当前语言文本。供 x:Bind 函数绑定调用：{x:Bind Strings.Get('Key'), Mode=OneWay}。</summary>
    public string Get(string key) => Loc.T(key);

    /// <summary>广播“全部已变”，供未来直接绑定本对象属性的场景；当前页面走宿主 PropertyChanged("Strings")。</summary>
    internal void RaiseAllChanged() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}
