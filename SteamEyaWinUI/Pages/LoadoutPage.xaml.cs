using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using SteamEyaWinUI.Localization;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;
using Windows.ApplicationModel.DataTransfer;

namespace SteamEyaWinUI.Pages;

public sealed partial class LoadoutPage : Page, INotifyPropertyChanged
{
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    private bool _currentCt;            // 当前编辑阵营：false=T，true=CT
    private bool _built;
    private CsLoadoutPreset _working = new();

    // 同进程拖放：DragStarting 记录，DragOver/Drop 直接用。
    private uint _draggingDef;
    private CsLoadoutGroup _draggingGroup;
    private uint? _draggingFromSlot;   // 非空=从已装备格子拖出（移动/交换）；空=从可选池拖出（装备）。

    private readonly Dictionary<uint, CellView> _cells = new();
    private readonly ObservableCollection<LoadoutWeaponTile> _poolItems = new();

    public LoadoutPage()
    {
        InitializeComponent();
        PoolGrid.ItemsSource = _poolItems;
        TeamSelector.SelectedItem = TeamTItem;
        Loc.LanguageChanged += OnLanguageChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>XAML 绑定入口：{x:Bind Strings.Get('Key'), Mode=OneWay}。</summary>
    internal LocalizedStrings Strings => Loc.Strings;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _working = AppState.SettingsService.Load().Loadout.Clone();
        BuildCells();
        RefreshAll();
    }

    private void OnLanguageChanged()
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            // 静态 x:Bind 文本随 Strings 重算；分组标题在代码里建，需重建；可选池武器名重新解析。
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Strings)));
            if (_built)
            {
                BuildCells();
                RefreshAll();
            }
        });
    }

    // 一个固定格子的可视元素引用。
    private sealed class CellView
    {
        public Border Root = null!;
        public Image Image = null!;
        public TextBlock Empty = null!;
        public Rectangle Line = null!;
        public uint Slot;
        public CsLoadoutGroup Group;
    }

    private static SolidColorBrush Brush(byte a, byte r, byte g, byte b) =>
        new(Windows.UI.Color.FromArgb(a, r, g, b));

    private void BuildCells()
    {
        _cells.Clear();
        BuildColumn(Col0Host,
        [
            (Loc.T("Loadout_Group_Starter"), CsLoadoutGroup.StarterPistol, [CsWeaponCatalog.StarterPistolSlot]),
            (Loc.T("Loadout_Group_Other"), CsLoadoutGroup.OtherPistol, CsWeaponCatalog.OtherPistolSlots)
        ]);
        BuildColumn(Col1Host, [(Loc.T("Loadout_Group_Mid"), CsLoadoutGroup.Mid, CsWeaponCatalog.MidSlots)]);
        BuildColumn(Col2Host, [(Loc.T("Loadout_Group_Rifle"), CsLoadoutGroup.Rifle, CsWeaponCatalog.RifleSlots)]);
        _built = true;
    }

    // 每节：一个 Auto 行标题 + 若干 Star 行格子。Star 行让格子拉伸填满列高 → 各列底部对齐。
    private void BuildColumn(Grid host, (string Title, CsLoadoutGroup Group, IReadOnlyList<uint> Slots)[] sections)
    {
        host.Children.Clear();
        host.RowDefinitions.Clear();

        var row = 0;
        foreach (var (title, group, slots) in sections)
        {
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var header = new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, row == 0 ? 0 : 6, 0, 4)
            };
            Grid.SetRow(header, row);
            host.Children.Add(header);
            row++;

            foreach (var slot in slots)
            {
                host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                var cell = BuildCell(group, slot);
                Grid.SetRow(cell.Root, row);
                host.Children.Add(cell.Root);
                _cells[slot] = cell;
                row++;
            }
        }
    }

    private CellView BuildCell(CsLoadoutGroup group, uint slot)
    {
        var bg = new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = Brush(0xFF, 0x24, 0x36, 0x4F),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush(0x33, 0xFF, 0xFF, 0xFF)
        };
        var image = new Image
        {
            Stretch = Stretch.Uniform,
            Margin = new Thickness(16, 8, 16, 12),
            HorizontalAlignment = HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        var empty = new TextBlock
        {
            Text = "",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var line = new Rectangle
        {
            Height = 2,
            Fill = Brush(0x80, 0xFF, 0xFF, 0xFF),
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(16, 0, 16, 4),
            Visibility = Visibility.Collapsed
        };

        var content = new Grid();
        content.Children.Add(bg);
        content.Children.Add(image);
        content.Children.Add(empty);
        content.Children.Add(line);

        var root = new Border
        {
            Child = content,
            Margin = new Thickness(0, 0, 0, 6),
            AllowDrop = true,
            CanDrag = true
        };

        var cell = new CellView { Root = root, Image = image, Empty = empty, Line = line, Slot = slot, Group = group };
        root.Tag = cell;
        root.DragStarting += Cell_DragStarting;
        root.DragOver += Cell_DragOver;
        root.Drop += Cell_Drop;
        root.Tapped += Cell_Tapped;
        return cell;
    }

    private void RefreshAll()
    {
        var slots = _working.SlotsFor(_currentCt);
        foreach (var cell in _cells.Values)
        {
            UpdateCell(cell, slots);
        }

        RebuildPool(slots);
    }

    private static void UpdateCell(CellView cell, Dictionary<uint, uint> slots)
    {
        if (slots.TryGetValue(cell.Slot, out var def) && CsWeaponCatalog.ByDef(def) is { } weapon)
        {
            cell.Image.Source = new SvgImageSource(new Uri(weapon.IconUri));
            cell.Image.Visibility = Visibility.Visible;
            cell.Empty.Visibility = Visibility.Collapsed;
            cell.Line.Visibility = Visibility.Visible;
        }
        else
        {
            cell.Image.Source = null;
            cell.Image.Visibility = Visibility.Collapsed;
            cell.Empty.Visibility = Visibility.Visible;
            cell.Line.Visibility = Visibility.Collapsed;
        }
    }

    private void RebuildPool(Dictionary<uint, uint> slots)
    {
        _poolItems.Clear();
        var equipped = slots.Values.ToHashSet();
        foreach (var group in CsWeaponCatalog.EditorGroups)
        {
            foreach (var weapon in CsWeaponCatalog.ForTeamGroup(_currentCt, group))
            {
                if (!equipped.Contains(weapon.Def))
                {
                    _poolItems.Add(new LoadoutWeaponTile(weapon));
                }
            }
        }
    }

    private void TeamSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        _currentCt = TeamSelector.SelectedItem == TeamCtItem;
        if (_built)
        {
            RefreshAll();
        }
    }

    // ---- 拖放 ----

    private void PoolGrid_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.Count > 0 && e.Items[0] is LoadoutWeaponTile tile)
        {
            _draggingDef = tile.Weapon.Def;
            _draggingGroup = CsWeaponCatalog.GroupOf(tile.Weapon);
            _draggingFromSlot = null;
            e.Data.SetText(tile.Weapon.Def.ToString());
            e.Data.RequestedOperation = DataPackageOperation.Copy;
        }
        else
        {
            e.Cancel = true;
        }
    }

    private void Cell_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if ((sender as FrameworkElement)?.Tag is not CellView cell ||
            !_working.SlotsFor(_currentCt).TryGetValue(cell.Slot, out var def))
        {
            args.Cancel = true;
            return;
        }

        _draggingDef = def;
        _draggingGroup = cell.Group;
        _draggingFromSlot = cell.Slot;
        args.Data.SetText(def.ToString());
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private void Cell_DragOver(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CellView cell && cell.Group == _draggingGroup)
        {
            e.AcceptedOperation = _draggingFromSlot is null ? DataPackageOperation.Copy : DataPackageOperation.Move;
            if (e.DragUIOverride is not null)
            {
                e.DragUIOverride.Caption = Loc.T(_draggingFromSlot is null ? "Loadout_Drag_Equip" : "Loadout_Drag_Move");
                e.DragUIOverride.IsContentVisible = true;
            }
        }
        else
        {
            e.AcceptedOperation = DataPackageOperation.None;
        }
    }

    private void Cell_Drop(object sender, DragEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not CellView cell || cell.Group != _draggingGroup)
        {
            return;
        }

        var slots = _working.SlotsFor(_currentCt);

        if (_draggingFromSlot is { } fromSlot)
        {
            // 已装备武器换位置：目标空则移动，目标有枪则交换。
            if (fromSlot == cell.Slot)
            {
                return;
            }

            if (slots.TryGetValue(cell.Slot, out var targetDef))
            {
                slots[cell.Slot] = _draggingDef;
                slots[fromSlot] = targetDef;
            }
            else
            {
                slots[cell.Slot] = _draggingDef;
                slots.Remove(fromSlot);
            }
        }
        else
        {
            // 从可选池装备（若原本有枪则被顶替，刷新后顶替下来的枪回到可选池）。
            slots[cell.Slot] = _draggingDef;
        }

        Persist();
        RefreshAll();
    }

    private void Cell_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is CellView cell &&
            _working.SlotsFor(_currentCt).Remove(cell.Slot))
        {
            Persist();
            RefreshAll();
        }
    }

    private void Persist()
    {
        var settings = AppState.SettingsService.Load();
        settings.Loadout = _working.Clone();
        AppState.SettingsService.Save(settings);
    }
}
