using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SteamEyaWinUI.Models;
using SteamEyaWinUI.Services;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace SteamEyaWinUI;

public sealed partial class MainWindow : Window
{
    private const int InitialWindowWidth = 1280;
    private const int InitialWindowHeight = 860;
    private const int MinWindowWidth = 1180;
    private const int MinWindowHeight = 780;
    private const int WmGetMinMaxInfo = 0x0024;
    private const nuint WindowSubclassId = 1;

    private readonly SteamLoginService _loginService = new();
    private readonly SteamWorkshopService _workshopService = new();
    private readonly SteamLicenseClient _licenseClient = new();
    private readonly JwtTokenService _jwtTokenService = new();
    private readonly SteamTokenOnlineValidationService _tokenOnlineValidationService = new();
    private readonly AccountHistoryService _accountHistoryService = new();
    private readonly CsPremierScoreService _premierScoreService = new();
    private readonly SubclassProc _subclassProc;
    private nint _hwnd;
    private bool _isApplyingSizeConstraint;
    private bool _isBusy;
    private List<SteamAccountHistoryItem> _historyAccounts = [];
    private SteamAccountData? _cachedAccountData;
    private string? _cachedLicenseKey;
    private SteamUpstreamServer? _cachedServer;

    public MainWindow()
    {
        _subclassProc = WindowSubclassProc;

        InitializeComponent();
        SystemBackdrop = new MicaBackdrop();

        UpstreamServerBox.ItemsSource = SteamLicenseClient.Servers;
        UpstreamServerBox.SelectedIndex = 2;
        RootNavigationView.SelectedItem = LoginNavItem;
        RefreshHistoryAccounts();
        UpdateAccountInfoFromCurrentInputs();

        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        SetWindowSubclass(_hwnd, _subclassProc, WindowSubclassId, 0);
        Closed += (_, _) => RemoveWindowSubclass(_hwnd, _subclassProc, WindowSubclassId);

        AppWindow.Resize(ToPhysicalSize(InitialWindowWidth, InitialWindowHeight));
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange)
            {
                ApplyWindowSizeConstraints();
            }
        };
    }

    private async void ClearWorkshopButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        ShowStatus("正在清除创意工坊订阅...", InfoBarSeverity.Informational);

        var progress = new Progress<string>(message =>
            ShowStatus(message, InfoBarSeverity.Informational));

        try
        {
            var (accountName, eyaToken) = await GetCredentialsAsync();
            EnsureTokenValidForAction(eyaToken, "清除创意工坊订阅");
            UpdateAccountInfo(accountName, eyaToken);
            await UpdateAccountProfileAsync(accountName, eyaToken);
            await EnsureTokenAcceptedBySteamAsync(eyaToken, "清除创意工坊订阅");
            var count = await _workshopService.ClearSubscriptionsAsync(eyaToken, progress);

            ShowStatus(
                $"已成功取消 {count} 个创意工坊订阅。",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void LoginButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        ShowStatus("正在处理登录...", InfoBarSeverity.Informational);

        var progress = new Progress<string>(message =>
            ShowStatus(message, InfoBarSeverity.Informational));

        try
        {
            var (accountName, eyaToken) = await GetCredentialsAsync();
            EnsureTokenValidForAction(eyaToken, "登录到 Steam");
            UpdateAccountInfo(accountName, eyaToken);
            await UpdateAccountProfileAsync(accountName, eyaToken);
            await EnsureTokenAcceptedBySteamAsync(eyaToken, "登录到 Steam");
            var result = await Task.Run(() => _loginService.Login(accountName, eyaToken, progress));
            var historyStatus = await SaveLoginHistoryAsync(result, eyaToken);
            ShowStatus(
                $"登录已启动。SteamID: {result.SteamId}，令牌剩余 {FormatRemaining(result.Remaining)}{historyStatus}。",
                historyStatus.StartsWith("，但", StringComparison.Ordinal)
                    ? InfoBarSeverity.Warning
                    : InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void ResolveLicenseButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        ShowStatus("正在解析卡密...", InfoBarSeverity.Informational);

        try
        {
            var account = await ResolveLicenseAsync();
            var online = await ValidateTokenOnlineAsync(account.Token);
            ShowStatus(
                online.IsValid
                    ? $"已解析账户：{account.User}，SteamID: {account.SteamId}。"
                    : online.Status,
                online.IsValid ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OneClickQueryButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        ShowStatus("正在一键查询账号状态...", InfoBarSeverity.Informational);

        try
        {
            var (accountName, eyaToken) = await GetCredentialsAsync();
            var score = await QueryAndSaveCsStatusAsync(accountName, eyaToken);
            ShowStatus(
                $"查询完成：优先分 {score.DisplayText}，CS2等级 {score.PlayerLevelText}，冷却 {score.CooldownText}，GC VAC {score.GcVacText}。",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void OneClickHistoryQueryButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryAccountList.SelectedItem is not SteamAccountHistoryItem account)
        {
            ShowStatus("请选择历史账号。", InfoBarSeverity.Error);
            return;
        }

        SetBusy(true);
        ShowStatus($"正在一键查询 {account.AccountTitle} 的账号状态...", InfoBarSeverity.Informational);

        try
        {
            var score = await QueryAndSaveCsStatusAsync(account.AccountName, account.EyaToken);
            ShowStatus(
                $"{account.AccountTitle} 查询完成：优先分 {score.DisplayText}，CS2等级 {score.PlayerLevelText}，冷却 {score.CooldownText}，GC VAC {score.GcVacText}。",
                InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void RefreshHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshHistoryAccounts(GetSelectedHistorySteamId());
        ShowStatus("历史账号已刷新。", InfoBarSeverity.Success);
    }

    private void HistoryAccountList_ItemClick(object sender, ItemClickEventArgs e)
    {
        HistoryAccountList.SelectedItem = e.ClickedItem;
    }

    private void HistoryAccountList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateHistoryDetail();
        UpdateHistoryControlsEnabled();
    }

    private void UseHistoryAccountButton_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryAccountList.SelectedItem is not SteamAccountHistoryItem account)
        {
            ShowStatus("请选择历史账号。", InfoBarSeverity.Error);
            return;
        }

        LoadHistoryAccount(account);
    }

    private void RootNavigationView_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string pageName)
        {
            ShowPage(pageName);
        }
    }

    private void ModeRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (ManualPanel is null || AutoPanel is null)
        {
            return;
        }

        var isAutoMode = AutoModeRadio.IsChecked == true;
        ManualPanel.Visibility = isAutoMode ? Visibility.Collapsed : Visibility.Visible;
        AutoPanel.Visibility = isAutoMode ? Visibility.Visible : Visibility.Collapsed;
        UpdateAccountInfoFromCurrentInputs();
        ShowStatus(isAutoMode ? "自动模式已启用。" : "手动模式已启用。", InfoBarSeverity.Informational);
    }

    private void ShowPage(string pageName)
    {
        var showHistory = pageName == "history";
        LoginPage.Visibility = showHistory ? Visibility.Collapsed : Visibility.Visible;
        HistoryPage.Visibility = showHistory ? Visibility.Visible : Visibility.Collapsed;

        if (showHistory)
        {
            RefreshHistoryAccounts(GetSelectedHistorySteamId());
        }
    }

    private void LoadHistoryAccount(SteamAccountHistoryItem account)
    {
        RootNavigationView.SelectedItem = LoginNavItem;
        ShowPage("login");
        ManualModeRadio.IsChecked = true;
        AccountNameBox.Text = account.AccountName;
        EyaTokenBox.Text = account.EyaToken;
        UpdateAccountInfo(account.AccountName, account.EyaToken);
        ApplyAccountInfoProfile(account);

        ShowStatus(
            $"已载入历史账号：{account.AccountName}，上次登录 {FormatDateTime(account.LastLoginAt)}。",
            InfoBarSeverity.Informational);
    }

    private void ManualCredentialBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (ManualModeRadio.IsChecked == true)
        {
            UpdateAccountInfoFromCurrentInputs();
        }
    }

    private void LicenseKeyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _cachedAccountData = null;
        _cachedLicenseKey = null;
        ResolvedAccountBox.Text = "";

        if (AutoModeRadio.IsChecked == true)
        {
            UpdateAccountInfoFromCurrentInputs();
        }
    }

    private void UpstreamServerBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _cachedAccountData = null;
        _cachedLicenseKey = null;
        ResolvedAccountBox.Text = "";

        if (AutoModeRadio.IsChecked == true)
        {
            UpdateAccountInfoFromCurrentInputs();
        }
    }

    private async Task<(string AccountName, string EyaToken)> GetCredentialsAsync()
    {
        if (AutoModeRadio.IsChecked == true)
        {
            var account = await ResolveLicenseAsync();
            return (account.User, NormalizeToken(account.Token));
        }

        var accountName = AccountNameBox.Text.Trim();
        var eyaToken = NormalizeToken(EyaTokenBox.Text.Trim());

        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new InvalidOperationException("请输入账户名称。");
        }

        if (string.IsNullOrWhiteSpace(eyaToken))
        {
            throw new InvalidOperationException("请输入 EYA 令牌。");
        }

        return (accountName, eyaToken);
    }

    private async Task<SteamAccountData> ResolveLicenseAsync()
    {
        var licenseKey = LicenseKeyBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            throw new InvalidOperationException("请输入卡密。");
        }

        var server = GetSelectedServer();
        if (_cachedAccountData is not null &&
            string.Equals(_cachedLicenseKey, licenseKey, StringComparison.Ordinal) &&
            Equals(_cachedServer, server))
        {
            return _cachedAccountData;
        }

        var account = await _licenseClient.GetAccountDataAsync(licenseKey, server);
        _cachedAccountData = account;
        _cachedLicenseKey = licenseKey;
        _cachedServer = server;
        ResolvedAccountBox.Text = $"{account.User}  ({account.SteamId})";
        UpdateAccountInfo(account.User, account.Token);
        await UpdateAccountProfileAsync(account.User, account.Token);
        return account;
    }

    private SteamUpstreamServer GetSelectedServer()
    {
        return UpstreamServerBox.SelectedItem as SteamUpstreamServer
            ?? throw new InvalidOperationException("请选择上游服务器。");
    }

    private static string NormalizeToken(string token)
    {
        return token.Replace(
            "EyAidHlwIjogIkpXVCIsICJhbGciOiAiRWREU0EiIH0",
            "eyAidHlwIjogIkpXVCIsICJhbGciOiAiRWREU0EiIH0",
            StringComparison.Ordinal);
    }

    private static string FormatRemaining(TimeSpan remaining)
    {
        return $"{Math.Floor(remaining.TotalDays)} 天 {remaining.Hours} 小时 {remaining.Minutes} 分钟";
    }

    private async Task<CsPremierScoreResult> QueryAndSaveCsStatusAsync(
        string accountName,
        string eyaToken)
    {
        eyaToken = NormalizeToken(eyaToken);
        var tokenInfo = _jwtTokenService.Inspect(eyaToken);
        if (!tokenInfo.IsValid)
        {
            throw new InvalidOperationException($"{tokenInfo.Status}无法一键查询。");
        }

        var steamId = tokenInfo.SteamId
            ?? throw new InvalidOperationException("EYA 令牌缺少 SteamID，无法查询账号状态。");

        UpdateAccountInfo(accountName, eyaToken);
        await UpdateAccountProfileAsync(accountName, eyaToken);
        AccountInfoPremierScoreText.Text = "等待令牌验证";
        AccountInfoCsLevelText.Text = "等待令牌验证";
        AccountInfoCooldownStatusText.Text = "等待令牌验证";

        var online = await ValidateTokenOnlineAsync(eyaToken);
        if (!online.IsValid)
        {
            throw new InvalidOperationException($"{online.Status}无法一键查询。");
        }

        AccountInfoPremierScoreText.Text = "正在查询";
        AccountInfoCsLevelText.Text = "正在查询";
        AccountInfoCooldownStatusText.Text = "正在查询";
        var score = await _premierScoreService.QueryAsync(eyaToken, steamId);
        _accountHistoryService.SaveCsAccountStatus(
            accountName,
            steamId,
            eyaToken,
            tokenInfo.ExpiresAt,
            score,
            online);

        RefreshHistoryAccounts(steamId);
        AccountInfoPremierScoreText.Text = score.DisplayText;
        AccountInfoCsLevelText.Text = score.PlayerLevelText;
        AccountInfoCooldownStatusText.Text = score.CooldownStatusText;
        ApplyStoredAccountInfoProfile(steamId);
        return score;
    }

    private async Task<string> SaveLoginHistoryAsync(LoginResult result, string eyaToken)
    {
        try
        {
            await _accountHistoryService.SaveLoginAsync(
                result.AccountName,
                result.SteamId,
                eyaToken,
                result.ExpiresAt);
            RefreshHistoryAccounts(result.SteamId);
            ApplyStoredAccountInfoProfile(result.SteamId);
            return "，已更新历史账号";
        }
        catch (Exception ex)
        {
            return $"，但历史账号保存失败：{ex.Message}";
        }
    }

    private void RefreshHistoryAccounts(string? selectedSteamId = null)
    {
        try
        {
            _historyAccounts = _accountHistoryService.Load().ToList();
        }
        catch (Exception ex)
        {
            _historyAccounts = [];
            ShowStatus($"历史账号读取失败：{ex.Message}", InfoBarSeverity.Warning);
        }

        HistoryAccountList.ItemsSource = null;
        HistoryAccountList.ItemsSource = _historyAccounts;

        var selectedAccount = !string.IsNullOrWhiteSpace(selectedSteamId)
            ? _historyAccounts.FirstOrDefault(account =>
                string.Equals(account.SteamId, selectedSteamId, StringComparison.OrdinalIgnoreCase))
            : null;
        HistoryAccountList.SelectedItem = selectedAccount ?? _historyAccounts.FirstOrDefault();

        HistoryEmptyPanel.Visibility = _historyAccounts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateHistoryDetail();
        UpdateHistoryControlsEnabled();
    }

    private void UpdateHistoryDetail()
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

    private void UpdateHistoryControlsEnabled()
    {
        var hasHistory = _historyAccounts.Count > 0;
        HistoryAccountList.IsEnabled = !_isBusy && hasHistory;
        RefreshHistoryButton.IsEnabled = !_isBusy;
        OneClickHistoryQueryButton.IsEnabled = !_isBusy &&
            HistoryAccountList.SelectedItem is SteamAccountHistoryItem;
        UseHistoryAccountButton.IsEnabled = !_isBusy &&
            HistoryAccountList.SelectedItem is SteamAccountHistoryItem;
    }

    private string? GetSelectedHistorySteamId()
    {
        return HistoryAccountList.SelectedItem is SteamAccountHistoryItem account
            ? account.SteamId
            : null;
    }

    private async Task UpdateAccountProfileAsync(string accountName, string eyaToken)
    {
        var tokenInfo = _jwtTokenService.Inspect(eyaToken);
        if (string.IsNullOrWhiteSpace(tokenInfo.SteamId))
        {
            return;
        }

        var storedAccount = FindHistoryAccount(tokenInfo.SteamId);
        if (storedAccount is not null)
        {
            ApplyAccountInfoProfile(storedAccount);
            if (!string.IsNullOrWhiteSpace(storedAccount.PersonaName) &&
                (!string.IsNullOrWhiteSpace(storedAccount.AvatarPath) ||
                    !string.IsNullOrWhiteSpace(storedAccount.AvatarUrl)))
            {
                return;
            }
        }

        try
        {
            var profile = await _accountHistoryService.GetProfilePreviewAsync(
                accountName,
                tokenInfo.SteamId,
                eyaToken,
                tokenInfo.ExpiresAt);
            if (profile is not null)
            {
                ApplyAccountInfoProfile(profile);
            }
        }
        catch
        {
            // Profile sync is decorative; token validation and account actions should continue.
        }
    }

    private bool ApplyStoredAccountInfoProfile(string steamId)
    {
        var account = FindHistoryAccount(steamId);
        if (account is null)
        {
            return false;
        }

        ApplyAccountInfoProfile(account);
        return true;
    }

    private SteamAccountHistoryItem? FindHistoryAccount(string steamId)
    {
        return _historyAccounts.FirstOrDefault(item =>
            string.Equals(item.SteamId, steamId, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyAccountInfoProfile(SteamAccountHistoryItem? account)
    {
        if (account is null)
        {
            AccountInfoAvatar.ProfilePicture = null;
            AccountInfoAvatar.DisplayName = "未填写";
            AccountInfoPersonaText.Text = "Steam 昵称未同步";
            AccountInfoPremierScoreText.Text = "未查询";
            AccountInfoCsLevelText.Text = "未查询";
            AccountInfoCooldownStatusText.Text = "未查询";
            return;
        }

        AccountInfoAvatar.DisplayName = account.AccountTitle;
        AccountInfoAvatar.ProfilePicture = account.AvatarImage;
        AccountInfoPersonaText.Text = account.PersonaDisplayName;
        AccountInfoPremierScoreText.Text = account.CompetitiveScoreText;
        AccountInfoCsLevelText.Text = account.CsPlayerLevelText;
        AccountInfoCooldownStatusText.Text = account.CooldownStatusText;
    }

    private static string FormatDateTime(DateTimeOffset value)
    {
        return value == default
            ? "未知时间"
            : value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    private void EnsureTokenValidForAction(string eyaToken, string actionName)
    {
        var info = _jwtTokenService.Inspect(eyaToken);
        if (info.IsValid)
        {
            return;
        }

        if (info.Status == "EYA 令牌已过期。" && actionName == "清除创意工坊订阅")
        {
            throw new InvalidOperationException("EYA 令牌已过期，无法清除创意工坊订阅。");
        }

        throw new InvalidOperationException($"{info.Status}无法{actionName}。");
    }

    private void UpdateAccountInfoFromCurrentInputs()
    {
        if (AccountInfoUserText is null)
        {
            return;
        }

        if (AutoModeRadio.IsChecked == true)
        {
            if (_cachedAccountData is not null)
            {
                UpdateAccountInfo(
                    _cachedAccountData.User,
                    _cachedAccountData.Token);
            }
            else
            {
                UpdateAccountInfo(null, null);
            }

            return;
        }

        UpdateAccountInfo(
            AccountNameBox.Text.Trim(),
            NormalizeToken(EyaTokenBox.Text.Trim()));
    }

    private void UpdateAccountInfo(string? userName, string? token)
    {
        AccountInfoUserText.Text = string.IsNullOrWhiteSpace(userName) ? "未填写" : userName;
        ApplyAccountInfoProfile(null);
        AccountInfoAvatar.DisplayName = string.IsNullOrWhiteSpace(userName) ? "未填写" : userName;

        if (string.IsNullOrWhiteSpace(token))
        {
            AccountInfoSteamIdText.Text = "未解析";
            AccountInfoExpiresText.Text = "未解析";
            AccountInfoAvailabilityText.Text = "未验证";
            AccountInfoAvailabilityText.Foreground = GetStatusBrush(InfoBarSeverity.Informational);
            return;
        }

        var info = _jwtTokenService.Inspect(token);
        AccountInfoSteamIdText.Text = string.IsNullOrWhiteSpace(info.SteamId) ? "未解析" : info.SteamId;
        AccountInfoExpiresText.Text = info.ExpiresAt.HasValue
            ? info.ExpiresAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
            : "未解析";
        AccountInfoAvailabilityText.Text = "未验证";
        AccountInfoAvailabilityText.Foreground = GetStatusBrush(InfoBarSeverity.Informational);

        if (!string.IsNullOrWhiteSpace(info.SteamId))
        {
            ApplyStoredAccountInfoProfile(info.SteamId);
        }
    }

    private async Task EnsureTokenAcceptedBySteamAsync(string eyaToken, string actionName)
    {
        var online = await ValidateTokenOnlineAsync(eyaToken);
        if (online.IsValid)
        {
            return;
        }

        throw new InvalidOperationException($"{online.Status}无法{actionName}。");
    }

    private async Task<SteamTokenOnlineValidationResult> ValidateTokenOnlineAsync(string eyaToken)
    {
        AccountInfoAvailabilityText.Text = "正在验证";
        AccountInfoAvailabilityText.Foreground = GetStatusBrush(InfoBarSeverity.Informational);

        try
        {
            var result = await _tokenOnlineValidationService.ValidateAsync(eyaToken);
            AccountInfoAvailabilityText.Text = result.IsValid ? "有效" : "无效";
            AccountInfoAvailabilityText.Foreground = GetStatusBrush(
                result.IsValid ? InfoBarSeverity.Success : InfoBarSeverity.Error);
            return result;
        }
        catch (Exception ex)
        {
            var result = new SteamTokenOnlineValidationResult(false, $"Steam 在线验证失败：{ex.Message}");
            AccountInfoAvailabilityText.Text = "无效";
            AccountInfoAvailabilityText.Foreground = GetStatusBrush(InfoBarSeverity.Error);
            return result;
        }
    }

    private void SetBusy(bool isBusy)
    {
        _isBusy = isBusy;
        LoginButton.IsEnabled = !isBusy;
        ClearWorkshopButton.IsEnabled = !isBusy;
        ResolveLicenseButton.IsEnabled = !isBusy;
        OneClickQueryButton.IsEnabled = !isBusy;
        ManualModeRadio.IsEnabled = !isBusy;
        AutoModeRadio.IsEnabled = !isBusy;
        UpstreamServerBox.IsEnabled = !isBusy;
        AccountNameBox.IsEnabled = !isBusy;
        EyaTokenBox.IsEnabled = !isBusy;
        LicenseKeyBox.IsEnabled = !isBusy;
        UpdateHistoryControlsEnabled();
        BusyRing.IsActive = isBusy;
        BusyRing.Visibility = isBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyWindowSizeConstraints()
    {
        if (_isApplyingSizeConstraint)
        {
            return;
        }

        var currentSize = AppWindow.Size;
        var minSize = ToPhysicalSize(MinWindowWidth, MinWindowHeight);
        var constrainedWidth = Math.Max(currentSize.Width, minSize.Width);
        var constrainedHeight = Math.Max(currentSize.Height, minSize.Height);

        if (currentSize.Width == constrainedWidth && currentSize.Height == constrainedHeight)
        {
            return;
        }

        _isApplyingSizeConstraint = true;
        try
        {
            AppWindow.Resize(new SizeInt32(constrainedWidth, constrainedHeight));
        }
        finally
        {
            _isApplyingSizeConstraint = false;
        }
    }

    private SizeInt32 ToPhysicalSize(int width, int height)
    {
        var scale = GetWindowScale();
        return new SizeInt32(
            (int)Math.Ceiling(width * scale),
            (int)Math.Ceiling(height * scale));
    }

    private double GetWindowScale()
    {
        if (_hwnd == 0)
        {
            return 1;
        }

        return GetDpiForWindow(_hwnd) / 96.0;
    }

    private nint WindowSubclassProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData)
    {
        if (message == WmGetMinMaxInfo)
        {
            var minSize = ToPhysicalSize(MinWindowWidth, MinWindowHeight);
            var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);

            info.MinTrackSize.X = minSize.Width;
            info.MinTrackSize.Y = minSize.Height;

            Marshal.StructureToPtr(info, lParam, true);
            return 0;
        }

        return DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusInfoBar.Message = message;
        StatusInfoBar.Severity = severity;
    }

    private static Brush GetStatusBrush(InfoBarSeverity severity)
    {
        var resourceKey = severity switch
        {
            InfoBarSeverity.Success => "SystemFillColorSuccessBrush",
            InfoBarSeverity.Error => "SystemFillColorCriticalBrush",
            _ => "TextFillColorSecondaryBrush"
        };

        if (Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Brush brush)
        {
            return brush;
        }

        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private delegate nint SubclassProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        nint hWnd,
        SubclassProc subclassProc,
        nuint subclassId,
        nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(
        nint hWnd,
        SubclassProc subclassProc,
        nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(
        nint hWnd,
        uint message,
        nint wParam,
        nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }
}
