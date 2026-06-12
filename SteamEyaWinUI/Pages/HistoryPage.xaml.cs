using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class HistoryPage : Page
{
    private readonly ObservableCollection<SteamAccountHistoryItem> _viewItems = [];

    public HistoryPage()
    {
        InitializeComponent();
        HistoryAccountList.ItemsSource = _viewItems;

        AppState.HistoryChanged += OnHistoryChanged;
        AppState.BusyChanged += _ => UpdateControlsEnabled();

        // 取用页面创建前积累的选中意图（首次构造时 _viewItems 为空，GetSelectedSteamId 必为 null）。
        var pending = AppState.PendingHistorySelection;
        AppState.PendingHistorySelection = null;
        RebuildView(pending);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        AppState.ReloadHistory(GetSelectedSteamId());
    }

    private void OnHistoryChanged(string? selectSteamId)
    {
        RebuildView(selectSteamId ?? GetSelectedSteamId());
    }

    private void RebuildView(string? selectSteamId)
    {
        var source = AppState.HistoryAccounts;
        var filter = HistorySearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? source
            : source.Where(account => Matches(account, filter)).ToList();

        _viewItems.Clear();
        foreach (var account in filtered)
        {
            _viewItems.Add(account);
        }

        var selectedAccount = !string.IsNullOrWhiteSpace(selectSteamId)
            ? _viewItems.FirstOrDefault(account =>
                string.Equals(account.SteamId, selectSteamId, StringComparison.OrdinalIgnoreCase))
            : null;
        HistoryAccountList.SelectedItem = selectedAccount ?? _viewItems.FirstOrDefault();

        var hasAny = source.Count > 0;
        HistoryEmptyPanel.Visibility = _viewItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryEmptyText.Text = hasAny ? "没有匹配的账号" : "暂无历史账号";
        HistoryEmptyHintText.Text = hasAny
            ? "换个关键词试试，或清空搜索框。"
            : "成功登录或一键查询后会自动记录账号。";
        HistorySummaryText.Text = hasAny
            ? $"共 {source.Count} 个账号，登录或查询过的账号会自动记录在这里。"
            : "登录或查询过的账号会自动记录在这里。";

        UpdateDetail();
        UpdateControlsEnabled();
    }

    private static bool Matches(SteamAccountHistoryItem account, string filter)
    {
        return Contains(account.AccountName, filter) ||
            Contains(account.PersonaName, filter) ||
            Contains(account.SteamId, filter);
    }

    private static bool Contains(string? value, string filter)
    {
        return !string.IsNullOrEmpty(value) &&
            value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private void HistorySearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            RebuildView(GetSelectedSteamId());
        }
    }

    private void RefreshHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        AppState.ReloadHistory(GetSelectedSteamId());
        AppState.ShowStatus("历史账号已刷新。", InfoBarSeverity.Success);
    }

    private void HistoryAccountList_ItemClick(object sender, ItemClickEventArgs e)
    {
        HistoryAccountList.SelectedItem = e.ClickedItem;
    }

    private void HistoryAccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDetail();
        UpdateControlsEnabled();
    }

    private async void OneClickHistoryQueryButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryAccountList.SelectedItem is not SteamAccountHistoryItem account)
        {
            AppState.ShowStatus("请选择历史账号。", InfoBarSeverity.Error);
            return;
        }

        if (AppState.LoginPage is not { } loginPage)
        {
            AppState.ShowStatus("登录页尚未初始化，请先打开登录页。", InfoBarSeverity.Error);
            return;
        }

        AppState.SetBusy(true);
        AppState.ShowStatus($"正在一键查询 {account.AccountTitle} 的账号状态...", InfoBarSeverity.Informational);

        try
        {
            var score = await loginPage.QueryAndSaveCsStatusAsync(account.AccountName, account.EyaToken);
            AppState.ShowStatus(
                $"{account.AccountTitle} 查询完成：优先分 {score.DisplayText}，CS2等级 {score.PlayerLevelText}，冷却 {score.CooldownText}，GC VAC {score.GcVacText}。",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            AppState.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            AppState.SetBusy(false);
        }
    }

    private void UseHistoryAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryAccountList.SelectedItem is not SteamAccountHistoryItem account)
        {
            AppState.ShowStatus("请选择历史账号。", InfoBarSeverity.Error);
            return;
        }

        MainWindow.Instance?.LoadAccountIntoLogin(account);
    }

    private string? GetSelectedSteamId()
    {
        return HistoryAccountList.SelectedItem is SteamAccountHistoryItem account
            ? account.SteamId
            : null;
    }

    private void UpdateDetail()
    {
        if (HistoryDetailAccountNameText is null)
        {
            return;
        }

        if (HistoryAccountList.SelectedItem is not SteamAccountHistoryItem account)
        {
            HistoryDetailAvatar.ProfilePicture = null;
            HistoryDetailAvatar.DisplayName = "未选择";
            HistoryDetailAccountNameText.Text = "未选择账号";
            HistoryDetailPersonaText.Text = "Steam 资料未同步";
            HistoryDetailSteamIdText.Text = "未解析";
            HistoryDetailTokenExpiresText.Text = "未解析";
            HistoryDetailLastLoginText.Text = "暂无记录";
            HistoryDetailCompetitiveScoreText.Text = "待查询";
            HistoryDetailCsLevelText.Text = "待查询";
            HistoryDetailCooldownStatusText.Text = "待查询";
            HistoryDetailAccountStatusText.Text = "待查询";
            return;
        }

        HistoryDetailAvatar.DisplayName = account.AccountTitle;
        HistoryDetailAvatar.ProfilePicture = account.AvatarImage;
        HistoryDetailAccountNameText.Text = account.AccountTitle;
        HistoryDetailPersonaText.Text = account.PersonaDisplayName;
        HistoryDetailSteamIdText.Text = account.SteamIdDisplay;
        HistoryDetailTokenExpiresText.Text = account.TokenExpiresText;
        HistoryDetailLastLoginText.Text = account.LastLoginText;
        HistoryDetailCompetitiveScoreText.Text = account.CompetitiveScoreText;
        HistoryDetailCsLevelText.Text = account.CsPlayerLevelText;
        HistoryDetailCooldownStatusText.Text = account.CooldownStatusText;
        HistoryDetailAccountStatusText.Text = account.JwtAvailabilityText;
    }

    private void UpdateControlsEnabled()
    {
        var isBusy = AppState.IsBusy;
        var hasSelection = HistoryAccountList.SelectedItem is SteamAccountHistoryItem;
        HistoryAccountList.IsEnabled = !isBusy && _viewItems.Count > 0;
        RefreshHistoryButton.IsEnabled = !isBusy;
        HistorySearchBox.IsEnabled = !isBusy;
        OneClickHistoryQueryButton.IsEnabled = !isBusy && hasSelection;
        UseHistoryAccountButton.IsEnabled = !isBusy && hasSelection;
    }
}
