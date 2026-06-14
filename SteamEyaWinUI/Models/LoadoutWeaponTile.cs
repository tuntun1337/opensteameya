using System.ComponentModel;
using Microsoft.UI.Xaml;

namespace SteamEyaWinUI.Models;

// 装备页面一个武器格子的视图模型：图标 + 名称 + 是否选中 + 在该类别中的位置序号。
// 仅 UI 线程访问。用计算属性暴露 Visibility，省去 AOT 下的值转换器。
internal sealed partial class LoadoutWeaponTile : INotifyPropertyChanged
{
    public LoadoutWeaponTile(CsWeapon weapon)
    {
        Weapon = weapon;
        IconUri = new Uri(weapon.IconUri);
    }

    public CsWeapon Weapon { get; }

    public Uri IconUri { get; }

    public string DisplayName => Weapon.LocalizedName;

    private bool _isSelected;
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
            Raise(nameof(IsSelected));
            Raise(nameof(SelectedVisibility));
        }
    }

    private string _positionText = "";
    public string PositionText
    {
        get => _positionText;
        set
        {
            if (_positionText == value)
            {
                return;
            }

            _positionText = value;
            Raise(nameof(PositionText));
            Raise(nameof(PositionVisibility));
        }
    }

    public Visibility SelectedVisibility => _isSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PositionVisibility =>
        string.IsNullOrEmpty(_positionText) ? Visibility.Collapsed : Visibility.Visible;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
