using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SteamEyaWinUI.Localization;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;
using Windows.ApplicationModel.DataTransfer;

namespace SteamEyaWinUI.Pages;

public sealed partial class HistoryPage : Page, INotifyPropertyChanged
{
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private readonly ObservableCollection<SteamAccountHistoryItem> _viewItems = [];
    private readonly ObservableCollection<AccountImportEntry> _importEntries = [];

    /// <summary>
    /// 当前从磁盘加载的完整列表快照（即 AppState.HistoryAccounts）。搜索/过滤只在此内存列表上做，
    /// 每个键击不再回读磁盘；仅 HistoryChanged 与进入页面时才刷新此快照。
    /// </summary>
    private IReadOnlyList<SteamAccountHistoryItem> _allItems = [];

    /// <summary>搜索框去抖计时器：输入停止约 300ms 后才执行一次过滤，避免逐键击全量重建。</summary>
    private readonly DispatcherQueueTimer _searchDebounceTimer;

    /// <summary>页面是否处于活动（已导航到、未离开）状态，用于不可见时延迟重建。</summary>
    private bool _isActive;

    /// <summary>
    /// 不可见期间收到 HistoryChanged 时只更新快照并记下待选中 SteamID（null 表示保持当前选择），
    /// 不做整列表重建；下次 OnNavigatedTo 经 ReloadHistory 统一重建一次。
    /// </summary>
    private string? _pendingSelectSteamId;

    /// <summary>
    /// 对话框流程重入门闩：同一 XamlRoot 同时只能打开一个 ContentDialog，二次 ShowAsync 直接抛异常。
    /// 导入流程在读剪贴板的 await 与 ShowAsync 之间存在挂起窗口，期间再点导入/删除/清空都必须拦下。
    /// </summary>
    private bool _isDialogFlowActive;

    /// <summary>
    /// 批量选择集（账号键）。与 ListView 的单选（详情焦点）解耦：勾选卡片左上角复选框进入此集，
    /// 驱动卡片黑框+对勾与底部批量操作栏。按键存储以便跨列表重建（换新实例）保留勾选。
    /// </summary>
    private readonly HashSet<string> _checkedKeys = new(StringComparer.OrdinalIgnoreCase);

    public HistoryPage()
    {
        InitializeComponent();
        HistoryAccountList.ItemsSource = _viewItems;
        ImportDialogList.ItemsSource = _importEntries;

        _searchDebounceTimer = DispatcherQueue.CreateTimer();
        _searchDebounceTimer.Interval = TimeSpan.FromMilliseconds(300);
        _searchDebounceTimer.IsRepeating = false;
        _searchDebounceTimer.Tick += (_, _) => RebuildView(GetSelectedSteamId());

        AppState.HistoryChanged += OnHistoryChanged;
        AppState.BusyChanged += _ => UpdateControlsEnabled();
        Loc.LanguageChanged += OnLanguageChanged;

        // 取用页面创建前积累的选中意图（首次构造时 _viewItems 为空，GetSelectedSteamId 必为 null）。
        var pending = AppState.PendingHistorySelection;
        AppState.PendingHistorySelection = null;
        _allItems = AppState.HistoryAccounts;
        RebuildView(pending);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>XAML 绑定入口：{x:Bind Strings.Get('Key'), Mode=OneWay}。</summary>
    internal LocalizedStrings Strings => Loc.Strings;

    private void OnLanguageChanged()
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            // 静态 x:Bind 文本随 Strings 重算；命令式文本（详情/批量栏/摘要/控件状态）重跑对应方法即可换语言。
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Strings)));
            UpdateSummaryTexts();
            UpdateBatchBar();
            UpdateDetail();
            UpdateControlsEnabled();
        });
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _isActive = true;

        // 不可见期间累积的待选中意图优先于当前选择；ReloadHistory 会刷新快照并触发重建。
        var select = _pendingSelectSteamId ?? GetSelectedSteamId();
        _pendingSelectSteamId = null;
        AppState.ReloadHistory(select);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _isActive = false;
        // 停掉去抖计时器，避免离开页面后 300ms 窗口内 Tick 仍对离屏页面触发一次无谓重建。
        _searchDebounceTimer.Stop();

        // 悬停时被导航走，PointerExited 可能不触发；清掉残留悬停态，避免回来后空心勾选圈残留。
        foreach (var account in _allItems)
        {
            account.IsPointerOver = false;
        }
    }

    private void CancelHistoryQueryButton_Click(object sender, RoutedEventArgs e)
    {
        AppState.ShowStatus(Loc.T("History_Status_Canceling"), InfoBarSeverity.Informational);
        AppState.CancelBusyOperation();
    }

    private void OnHistoryChanged(string? selectSteamId)
    {
        // 进入页面时 OnNavigatedTo 触发的 ReloadHistory 会先于此处把 _isActive 置 true，正常重建；
        // 页面不可见时（其它页触发的后台刷新）只更新内存快照并记下待选中意图，下次 OnNavigatedTo 再重建。
        _allItems = AppState.HistoryAccounts;
        if (!_isActive)
        {
            _pendingSelectSteamId = selectSteamId ?? _pendingSelectSteamId;
            return;
        }

        RebuildView(selectSteamId ?? GetSelectedSteamId());
    }

    private void RebuildView(string? selectSteamId)
    {
        // 过滤只在内存快照上做，不回读磁盘。
        var source = _allItems;
        var filter = HistorySearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(filter)
            ? source
            : source.Where(account => Matches(account, filter)).ToList();

        // 批量勾选集按账号键跨重建保留：先剔除已不存在的账号，再把勾选状态套用到（可能是新的）实例。
        var liveKeys = source
            .Select(AccountHistoryService.GetAccountKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _checkedKeys.IntersectWith(liveKeys);
        foreach (var account in source)
        {
            account.IsSelected = _checkedKeys.Contains(AccountHistoryService.GetAccountKey(account));
        }

        // 记住当前单选（详情焦点）的账号键，重建后恢复——后台资料同步等延迟刷新不应丢失当前查看的账号。
        var activeKey = !string.IsNullOrWhiteSpace(selectSteamId)
            ? $"id:{selectSteamId}"
            : HistoryAccountList.SelectedItem is SteamAccountHistoryItem current
                ? AccountHistoryService.GetAccountKey(current)
                : null;

        _viewItems.Clear();
        foreach (var account in filtered)
        {
            _viewItems.Add(account);
        }

        var active = activeKey is null
            ? null
            : _viewItems.FirstOrDefault(account =>
                string.Equals(AccountHistoryService.GetAccountKey(account), activeKey, StringComparison.OrdinalIgnoreCase));
        HistoryAccountList.SelectedItem = active ?? _viewItems.FirstOrDefault();

        HistoryEmptyPanel.Visibility = _viewItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSummaryTexts();

        UpdateBatchBar();
        UpdateDetail();
        UpdateControlsEnabled();
    }

    /// <summary>按当前快照重算页头摘要与空状态文案（语言切换时也复用此处刷新已显示文本）。</summary>
    private void UpdateSummaryTexts()
    {
        var hasAny = _allItems.Count > 0;
        HistoryEmptyText.Text = hasAny ? Loc.T("History_Empty_NoMatch") : Loc.T("History_Empty_Title");
        HistoryEmptyHintText.Text = hasAny
            ? Loc.T("History_Empty_NoMatch_Hint")
            : Loc.T("History_Empty_Hint");
        HistorySummaryText.Text = hasAny
            ? Loc.Tf("History_Subtitle_Count_Format", _allItems.Count)
            : Loc.T("History_Subtitle");
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
            // 去抖：连续输入只 Start/重置计时器，停止输入约 300ms 后才真正过滤重建。
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }
    }

    private void RefreshHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        AppState.ReloadHistory(GetSelectedSteamId());
        AppState.ShowStatus(Loc.T("History_Status_Refreshed"), InfoBarSeverity.Success);
    }

    private void HistoryAccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateDetail();
        UpdateControlsEnabled();
    }

    private void ExportAccountsToClipboard(IReadOnlyList<SteamAccountHistoryItem> accounts)
    {
        if (accounts.Count == 0)
        {
            AppState.ShowStatus(Loc.T("History_Status_NoneToExport"), InfoBarSeverity.Error);
            return;
        }

        var text = string.Join(
            Environment.NewLine,
            accounts.Select(account => $"{account.AccountName}----{account.EyaToken}"));

        var package = new DataPackage();
        package.SetText(text);

        try
        {
            // 令牌为敏感凭据：不进 Win+V 剪贴板历史、不随云剪贴板漫游。
            var options = new ClipboardContentOptions
            {
                IsAllowedInHistory = false,
                IsRoamable = false
            };
            if (!Clipboard.SetContentWithOptions(package, options))
            {
                // 个别系统配置下 SetContentWithOptions 可能返回 false；回退到普通写入保证导出可用。
                Clipboard.SetContent(package);
            }
        }
        catch (COMException)
        {
            AppState.ShowStatus(Loc.T("History_Status_ClipboardWriteFail"), InfoBarSeverity.Error);
            return;
        }

        try
        {
            // 不 Flush 的话内容由本进程延迟渲染，应用退出后剪贴板就空了；Flush 失败不影响本次粘贴。
            Clipboard.Flush();
        }
        catch (COMException)
        {
        }

        AppState.ShowStatus(
            accounts.Count == 1
                ? Loc.Tf("History_Status_Exported_One_Format", accounts[0].AccountTitle)
                : Loc.Tf("History_Status_Exported_Many_Format", accounts.Count),
            InfoBarSeverity.Success);
    }

    private async void ImportHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDialogFlowActive)
        {
            return;
        }

        List<AccountImportEntry>? selected;
        _isDialogFlowActive = true;
        try
        {
            selected = await PickImportEntriesAsync();
        }
        finally
        {
            _isDialogFlowActive = false;
        }

        if (selected is null || selected.Count == 0)
        {
            return;
        }

        AppState.SetBusy(true);
        try
        {
            var (added, updated) = AppState.AccountHistoryService.ImportAccounts(selected);
            AppState.ReloadHistory(selected[0].SteamId);
            AppState.ShowStatus(
                Loc.Tf("History_Status_ImportDone_Format", added, updated),
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            AppState.ShowStatus(Loc.Tf("History_Status_ImportFail_Format", ex.Message), InfoBarSeverity.Error);
            return;
        }
        finally
        {
            AppState.SetBusy(false);
        }

        // 后台补全昵称/头像，完成后刷新列表（不占用全局忙碌状态，失败不影响导入结果）。
        try
        {
            var refreshed = await AppState.AccountHistoryService.RefreshProfilesAsync(
                selected.Select(entry => entry.SteamId).ToList());
            if (refreshed > 0)
            {
                AppState.ReloadHistory(GetSelectedSteamId());
                AppState.ShowStatus(Loc.Tf("History_Status_ProfileSyncDone_Format", refreshed), InfoBarSeverity.Success);
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>读剪贴板 → 解析 → 弹勾选对话框；返回用户确认导入的条目，取消或无可导入内容时返回 null。</summary>
    private async Task<List<AccountImportEntry>?> PickImportEntriesAsync()
    {
        string clipboardText;
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text))
            {
                AppState.ShowStatus(Loc.T("History_Status_ClipboardNoText"), InfoBarSeverity.Error);
                return null;
            }

            clipboardText = await content.GetTextAsync();
        }
        catch (COMException)
        {
            AppState.ShowStatus(Loc.T("History_Status_ClipboardReadFail"), InfoBarSeverity.Error);
            return null;
        }

        var (entries, invalidCount) = ParseImportText(clipboardText);
        if (entries.Count == 0)
        {
            AppState.ShowStatus(
                invalidCount > 0
                    ? Loc.Tf("History_Status_ImportNoneRecognized_Format", invalidCount)
                    : Loc.T("History_Status_ImportNone"),
                InfoBarSeverity.Error);
            return null;
        }

        _importEntries.Clear();
        foreach (var entry in entries)
        {
            _importEntries.Add(entry);
        }

        ImportDialogSummaryText.Text = invalidCount > 0
            ? Loc.Tf("History_ImportDialog_Summary_WithInvalid_Format", entries.Count, invalidCount)
            : Loc.Tf("History_ImportDialog_Summary_Format", entries.Count);
        ImportDialogList.SelectAll();

        if (await ImportDialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        return ImportDialogList.SelectedItems.OfType<AccountImportEntry>().ToList();
    }

    private void ImportDialogList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ImportDialog.IsPrimaryButtonEnabled = ImportDialogList.SelectedItems.Count > 0;
    }

    private async Task DeleteAccountsWithConfirmAsync(IReadOnlyList<SteamAccountHistoryItem> accounts)
    {
        if (_isDialogFlowActive)
        {
            return;
        }

        if (accounts.Count == 0)
        {
            AppState.ShowStatus(Loc.T("History_Status_NoneToDelete"), InfoBarSeverity.Error);
            return;
        }

        var nameText = string.Join("、", accounts.Take(5).Select(account => account.AccountTitle));
        var summary = accounts.Count > 5
            ? Loc.Tf("History_Delete_Confirm_Many_Format", nameText, accounts.Count)
            : Loc.Tf("History_Delete_Confirm_Few_Format", nameText, accounts.Count);

        var dialog = new ContentDialog
        {
            Title = Loc.T("History_Delete_Dialog_Title"),
            Content = summary,
            PrimaryButtonText = Loc.T("Common_Delete"),
            CloseButtonText = Loc.T("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        _isDialogFlowActive = true;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            // 删除前把这些键移出批量选择集，避免重建后残留在已选状态。
            foreach (var account in accounts)
            {
                _checkedKeys.Remove(AccountHistoryService.GetAccountKey(account));
            }

            var removed = AppState.AccountHistoryService.DeleteAccounts(accounts);
            AppState.ReloadHistory();
            AppState.ShowStatus(Loc.Tf("History_Status_Deleted_Format", removed), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            AppState.ShowStatus(Loc.Tf("History_Status_DeleteFail_Format", ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            _isDialogFlowActive = false;
        }
    }

    // ---------- 卡片悬停 / 左上角勾选 / 单卡操作 ----------

    private static SteamAccountHistoryItem? CardItem(object sender) =>
        (sender as FrameworkElement)?.DataContext as SteamAccountHistoryItem;

    private void HistoryCard_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (CardItem(sender) is { } account)
        {
            account.IsPointerOver = true;
        }
    }

    private void HistoryCard_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (CardItem(sender) is { } account)
        {
            account.IsPointerOver = false;
        }
    }

    private void HistoryCardCheck_Click(object sender, RoutedEventArgs e)
    {
        if (CardItem(sender) is not { } account)
        {
            return;
        }

        var key = AccountHistoryService.GetAccountKey(account);
        if (account.IsSelected)
        {
            account.IsSelected = false;
            _checkedKeys.Remove(key);
        }
        else
        {
            account.IsSelected = true;
            _checkedKeys.Add(key);
        }

        UpdateBatchBar();
    }

    private async void CardQuickLoginButton_Click(object sender, RoutedEventArgs e)
    {
        if (CardItem(sender) is not { } account)
        {
            return;
        }

        if (AppState.LoginPage is not { } loginPage)
        {
            AppState.ShowStatus(Loc.T("History_Status_LoginPageNotReady"), InfoBarSeverity.Error);
            return;
        }

        if (string.IsNullOrWhiteSpace(account.AccountName))
        {
            AppState.ShowStatus(Loc.T("History_Status_MissingAccountName"), InfoBarSeverity.Error);
            return;
        }

        var cancellationToken = AppState.BeginBusyOperation();
        AppState.ShowStatus(Loc.Tf("History_Status_LoggingIn_Format", account.AccountTitle), InfoBarSeverity.Informational);
        var progress = new Progress<string>(message => AppState.ShowStatus(message, InfoBarSeverity.Informational));

        try
        {
            var result = await loginPage.QuickLoginAsync(
                account.AccountName, account.EyaToken, progress, cancellationToken);
            AppState.ReloadHistory(result.SteamId);
            AppState.ShowStatus(
                Loc.Tf("History_Status_LoginStarted_Format", account.AccountTitle, result.SteamId),
                InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            AppState.ShowStatus(Loc.T("History_Status_LoginCanceled"), InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            AppLog.Error("快速登录失败。", ex);
            AppState.ShowStatus(Loc.Tf("History_Status_LoginFail_Format", ex.Message, AppLog.LogFilePath), InfoBarSeverity.Error);
        }
        finally
        {
            AppState.EndBusyOperation();
        }
    }

    private void CardExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (CardItem(sender) is { } account)
        {
            ExportAccountsToClipboard(new[] { account });
        }
    }

    private async void CardDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (CardItem(sender) is { } account)
        {
            await DeleteAccountsWithConfirmAsync(new[] { account });
        }
    }

    // ---------- 底部批量操作栏（勾选任意卡片后浮现，操作针对全部已勾选账号） ----------

    // 只作用于当前可见（已过滤）列表：避免搜索过滤下对看不见的勾选项执行批量删除/导出。
    // 被过滤隐藏的勾选项仍保留在 _checkedKeys，清空搜索后会重新出现并计入。
    private List<SteamAccountHistoryItem> GetCheckedAccounts() =>
        _viewItems
            .Where(account => _checkedKeys.Contains(AccountHistoryService.GetAccountKey(account)))
            .ToList();

    private void UpdateBatchBar()
    {
        // 计数与 GetCheckedAccounts 同口径（可见集），保证"已选 N 项"与批量操作实际作用集一致。
        var count = GetCheckedAccounts().Count;
        BatchActionBar.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BatchSelectionText.Text = Loc.Tf("Common_Selected_Format", count);
    }

    private void ClearCheckedSelection()
    {
        _checkedKeys.Clear();
        foreach (var account in _allItems)
        {
            account.IsSelected = false;
        }

        UpdateBatchBar();
    }

    private void BatchClearButton_Click(object sender, RoutedEventArgs e)
    {
        ClearCheckedSelection();
    }

    private void BatchExportButton_Click(object sender, RoutedEventArgs e)
    {
        ExportAccountsToClipboard(GetCheckedAccounts());
    }

    private async void BatchDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        await DeleteAccountsWithConfirmAsync(GetCheckedAccounts());
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDialogFlowActive)
        {
            return;
        }

        var total = AppState.HistoryAccounts.Count;
        if (total == 0)
        {
            AppState.ShowStatus(Loc.T("History_Status_NoneToClear"), InfoBarSeverity.Error);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = Loc.T("History_ClearAll_Dialog_Title"),
            Content = Loc.Tf("History_ClearAll_Dialog_Content_Format", total),
            PrimaryButtonText = Loc.T("Common_Clear"),
            CloseButtonText = Loc.T("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        _isDialogFlowActive = true;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var cleared = AppState.AccountHistoryService.ClearAll();
            AppState.ReloadHistory();
            AppState.ShowStatus(Loc.Tf("History_Status_Cleared_Format", cleared), InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            AppState.ShowStatus(Loc.Tf("History_Status_ClearFail_Format", ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            _isDialogFlowActive = false;
        }
    }

    private async void ClearInvalidAccountsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isDialogFlowActive)
        {
            return;
        }

        var accounts = AppState.HistoryAccounts.ToList();
        if (accounts.Count == 0)
        {
            AppState.ShowStatus(Loc.T("History_Status_NoneToTest"), InfoBarSeverity.Error);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = Loc.T("History_ClearInvalid_Dialog_Title"),
            Content = Loc.Tf("History_ClearInvalid_Dialog_Content_Format", accounts.Count) +
                Environment.NewLine + Environment.NewLine +
                Loc.T("History_ClearInvalid_Dialog_Content_Note"),
            PrimaryButtonText = Loc.T("History_ClearInvalid_Dialog_Primary"),
            CloseButtonText = Loc.T("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        _isDialogFlowActive = true;
        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }
        finally
        {
            _isDialogFlowActive = false;
        }

        // 测试期间复用全局忙碌+取消机制（取消按钮可中断）；网络错误或取消时全程不删除任何账号，
        // 仅在完整测试通过后才一次性删除被 Steam 拒绝（含令牌畸形/过期）的账号。
        var cancellationToken = AppState.BeginBusyOperation();
        var invalid = new List<SteamAccountHistoryItem>();
        var tested = 0;
        string? networkError = null;
        var canceled = false;

        try
        {
            foreach (var account in accounts)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    canceled = true;
                    break;
                }

                AppState.ShowStatus(
                    Loc.Tf("History_Status_Testing_Format", tested + 1, accounts.Count, account.AccountTitle),
                    InfoBarSeverity.Informational);

                SteamTokenOnlineValidationResult result;
                try
                {
                    result = await AppState.TokenOnlineValidationService.ValidateAsync(
                        account.EyaToken, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    canceled = true;
                    break;
                }
                catch (Exception ex)
                {
                    // 网络错误（CM 不可达/超时等）：立即停止，不删除任何账号。
                    networkError = ex.Message;
                    break;
                }

                tested++;
                if (!result.IsValid)
                {
                    invalid.Add(account);
                }
            }

            // 无论是完整跑完、遇网络错误还是被取消，已确认被 Steam 拒绝的账号都照删；
            // 停止只是不再测试剩余账号（未测试的账号一律保留）。
            var removed = invalid.Count > 0
                ? AppState.AccountHistoryService.DeleteAccounts(invalid)
                : 0;
            if (removed > 0)
            {
                AppState.ReloadHistory();
            }

            if (networkError is not null)
            {
                AppLog.Warn($"批量测试历史账号时遇到网络错误，已停止：{networkError}");
                AppState.ShowStatus(
                    removed > 0
                        ? Loc.Tf("History_Status_TestNetworkErr_WithRemoved_Format", tested, networkError, removed)
                        : Loc.Tf("History_Status_TestNetworkErr_Format", tested, networkError),
                    InfoBarSeverity.Error);
                return;
            }

            if (canceled)
            {
                AppState.ShowStatus(
                    removed > 0
                        ? Loc.Tf("History_Status_TestCanceled_WithRemoved_Format", tested, removed)
                        : Loc.Tf("History_Status_TestCanceled_Format", tested),
                    InfoBarSeverity.Informational);
                return;
            }

            AppState.ShowStatus(
                removed > 0
                    ? Loc.Tf("History_Status_TestDone_WithRemoved_Format", tested, removed)
                    : Loc.Tf("History_Status_TestDone_AllValid_Format", tested),
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            AppState.ShowStatus(Loc.Tf("History_Status_ClearInvalidFail_Format", ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            AppState.EndBusyOperation();
        }
    }

    private static (List<AccountImportEntry> Entries, int InvalidCount) ParseImportText(string text)
    {
        var entries = new List<AccountImportEntry>();
        var seenSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var invalidCount = 0;

        foreach (var rawLine in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (!TryParseCredentials(line, out var accountName, out var token, out var info))
            {
                invalidCount++;
                continue;
            }

            var steamId = info.SteamId!;

            // 同一账号出现多行时取第一行。
            if (!seenSteamIds.Add(steamId))
            {
                continue;
            }

            entries.Add(new AccountImportEntry
            {
                AccountName = accountName,
                EyaToken = token,
                SteamId = steamId,
                TokenExpiresAt = info.ExpiresAt,
                TokenIsValid = info.IsValid,
                TokenStatus = info.Status,
                AlreadyExists = AppState.HistoryAccounts.Any(account =>
                    string.Equals(account.SteamId, steamId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(account.AccountName, accountName, StringComparison.OrdinalIgnoreCase))
            });
        }

        return (entries, invalidCount);
    }

    /// <summary>
    /// 解析一行凭据。逐个尝试候选切分（“----”各列、空白各列），以“令牌能解析出 SteamID”为准。
    /// 单列候选排在拼接候选之前，确保“账号----密码----令牌”这类多列行取到干净的令牌
    /// 而不是把“密码----令牌”整串当令牌存入。
    /// </summary>
    private static bool TryParseCredentials(
        string line,
        out string accountName,
        out string token,
        out JwtTokenInfo info)
    {
        foreach (var (name, candidateToken) in EnumerateCredentialCandidates(line))
        {
            if (name.Length == 0 || candidateToken.Length == 0)
            {
                continue;
            }

            var normalized = FormatHelper.NormalizeToken(candidateToken);
            JwtTokenInfo candidateInfo;
            try
            {
                candidateInfo = AppState.JwtTokenService.Inspect(normalized);
            }
            catch (Exception)
            {
                // 剪贴板内容不可信，畸形伪 JWT 一律按无法识别处理，绝不允许解析把应用带崩。
                continue;
            }

            if (!string.IsNullOrWhiteSpace(candidateInfo.SteamId))
            {
                accountName = name;
                token = normalized;
                info = candidateInfo;
                return true;
            }
        }

        accountName = "";
        token = "";
        info = new JwtTokenInfo(null, null, false, Loc.T("Jwt_Status_Unrecognized"), null);
        return false;
    }

    private static IEnumerable<(string AccountName, string Token)> EnumerateCredentialCandidates(string line)
    {
        // “账户名----令牌”或“账户名----密码----令牌”等多列格式：
        // 账户名取第一列，令牌依次尝试其余各列。
        var columns = line.Split("----", StringSplitOptions.None);
        if (columns.Length >= 2)
        {
            var name = columns[0].Trim();
            for (var i = 1; i < columns.Length; i++)
            {
                yield return (name, columns[i].Trim());
            }

            // 极端情况：令牌本身含“----”（base64url 字符集含 '-'），把第一列之后的内容整体再试一次。
            if (columns.Length > 2)
            {
                yield return (name, string.Join("----", columns[1..]).Trim());
            }
        }

        // 兼容空格 / Tab 分隔的“账户名 令牌 [备注…]”格式：令牌依次尝试各列。
        var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length >= 2)
        {
            for (var i = 1; i < fields.Length; i++)
            {
                yield return (fields[0].Trim(), fields[i].Trim());
            }
        }
    }

    private async void OneClickHistoryQueryButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryAccountList.SelectedItem is not SteamAccountHistoryItem account)
        {
            AppState.ShowStatus(Loc.T("History_Status_SelectAccount"), InfoBarSeverity.Error);
            return;
        }

        if (AppState.LoginPage is not { } loginPage)
        {
            AppState.ShowStatus(Loc.T("History_Status_LoginPageNotReady"), InfoBarSeverity.Error);
            return;
        }

        // 与登录页统一走可取消的忙碌机制：一键查询最长可达上百秒，必须能被取消按钮中断。
        var cancellationToken = AppState.BeginBusyOperation();
        AppState.ShowStatus(Loc.Tf("History_Status_Querying_Format", account.AccountTitle), InfoBarSeverity.Informational);

        try
        {
            var score = await loginPage.QueryAndSaveCsStatusAsync(
                account.AccountName, account.EyaToken, cancellationToken);
            AppState.ShowStatus(
                Loc.Tf("History_Status_QueryDone_Format", account.AccountTitle, score.DisplayText, score.PlayerLevelText, score.CooldownText, score.GcVacText),
                InfoBarSeverity.Success);
        }
        catch (OperationCanceledException)
        {
            AppState.ShowStatus(Loc.T("History_Status_QueryCanceled"), InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            AppState.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            AppState.EndBusyOperation();
        }
    }

    private void UseHistoryAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryAccountList.SelectedItem is not SteamAccountHistoryItem account)
        {
            AppState.ShowStatus(Loc.T("History_Status_SelectAccount"), InfoBarSeverity.Error);
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
            HistoryDetailAvatar.DisplayName = Loc.T("History_Detail_Unselected");
            HistoryDetailAccountNameText.Text = Loc.T("History_Detail_NoAccountSelected");
            HistoryDetailPersonaText.Text = Loc.T("History_Detail_ProfileNotSynced");
            HistoryDetailSteamIdText.Text = Loc.T("History_Detail_Unparsed");
            HistoryDetailTokenExpiresText.Text = Loc.T("History_Detail_Unparsed");
            HistoryDetailLastLoginText.Text = Loc.T("History_Detail_NoRecord");
            HistoryDetailCompetitiveScoreText.Text = Loc.T("History_Detail_Pending");
            HistoryDetailCsLevelText.Text = Loc.T("History_Detail_Pending");
            HistoryDetailCooldownStatusText.Text = Loc.T("History_Detail_Pending");
            HistoryDetailAccountStatusText.Text = Loc.T("History_Detail_Pending");
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
        var hasActive = HistoryAccountList.SelectedItem is SteamAccountHistoryItem;
        HistoryAccountList.IsEnabled = !isBusy && _viewItems.Count > 0;
        RefreshHistoryButton.IsEnabled = !isBusy;
        HistorySearchBox.IsEnabled = !isBusy;
        ImportHistoryButton.IsEnabled = !isBusy;
        ClearHistoryButton.IsEnabled = !isBusy && AppState.HistoryAccounts.Count > 0;
        ClearInvalidAccountsButton.IsEnabled = !isBusy && AppState.HistoryAccounts.Count > 0;
        OneClickHistoryQueryButton.IsEnabled = !isBusy && hasActive;
        UseHistoryAccountButton.IsEnabled = !isBusy && hasActive;
        BatchClearButton.IsEnabled = !isBusy;
        BatchExportButton.IsEnabled = !isBusy;
        BatchDeleteButton.IsEnabled = !isBusy;

        // 取消按钮仅忙碌时出现且保持可用，让用户中断本页发起的一键查询。
        CancelHistoryQueryButton.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }
}
