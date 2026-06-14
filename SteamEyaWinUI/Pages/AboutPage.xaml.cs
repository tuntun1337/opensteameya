using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SteamEyaWinUI.Localization;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class AboutPage : Page, INotifyPropertyChanged
{
    private readonly DispatcherQueue _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

    public AboutPage()
    {
        InitializeComponent();
        AppState.UpdateStateChanged += Render;
        Loc.LanguageChanged += OnLanguageChanged;
        Render();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>XAML 绑定入口：{x:Bind Strings.Get('Key'), Mode=OneWay}。</summary>
    internal LocalizedStrings Strings => Loc.Strings;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (AppState.LatestUpdate is null && !AppState.IsCheckingForUpdates)
        {
            _ = AppState.CheckForUpdatesAsync(isAutomatic: true);
        }
    }

    private void OnLanguageChanged()
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            // 静态 x:Bind 文本随 Strings 重算；命令式文本（版本/更新状态/日志）重跑 Render 即可换语言。
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Strings)));
            Render();
        });
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await AppState.CheckForUpdatesAsync(isAutomatic: false);
    }

    private async void DownloadUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        var url = AppState.LatestUpdate?.ArtifactUrl ?? AppState.LatestUpdate?.ReleaseUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            AppState.ShowStatus(Loc.T("About_NoDownloadInfo"), InfoBarSeverity.Warning);
            return;
        }

        await AppState.OpenUrlAsync(url);
    }

    private async void OpenReleaseButton_Click(object sender, RoutedEventArgs e)
    {
        await AppState.OpenUrlAsync(AppState.LatestUpdate?.ReleaseUrl ?? GitHubUpdateService.ReleasesUrl);
    }

    private async void OpenGitHubButton_Click(object sender, RoutedEventArgs e)
    {
        await AppState.OpenUrlAsync(GitHubUpdateService.RepositoryUrl);
    }

    private void Render()
    {
        var update = AppState.LatestUpdate;
        var isChecking = AppState.IsCheckingForUpdates;

        UpdateCheckingRing.IsActive = isChecking;
        UpdateCheckingRing.Visibility = isChecking ? Visibility.Visible : Visibility.Collapsed;
        CheckUpdateButton.IsEnabled = !isChecking;
        DownloadUpdateButton.IsEnabled = !isChecking && !string.IsNullOrWhiteSpace(update?.ArtifactUrl);

        AboutVersionText.Text = Loc.Tf("About_Version_Format", update?.CurrentVersion ?? GitHubUpdateService.CurrentVersion);

        if (isChecking)
        {
            AboutUpdateStatusText.Text = Loc.T("About_Update_Connecting");
            AboutUpdateCheckedText.Text = Loc.T("About_CheckedAt_Checking");
            return;
        }

        if (AppState.UpdateCheckError is { } error)
        {
            AboutUpdateStatusText.Text = Loc.Tf("About_Update_ConnectFail_Format", error);
            AboutArtifactText.Text = Loc.T("About_Artifact_ReadFail");
            AboutUpdateCheckedText.Text = AppState.UpdateCheckedAt.HasValue
                ? Loc.Tf("About_CheckedAt_Format", FormatHelper.FormatDateTime(AppState.UpdateCheckedAt.Value))
                : Loc.T("About_CheckedAt_Never");
            AboutChangelogText.Text = Loc.T("About_Changelog_ReadFail");
            return;
        }

        if (update is null)
        {
            AboutUpdateStatusText.Text = Loc.T("About_Update_AutoHint");
            AboutArtifactText.Text = Loc.T("About_Artifact_Never");
            AboutUpdateCheckedText.Text = Loc.T("About_CheckedAt_Never");
            AboutChangelogText.Text = Loc.T("About_Changelog_Never");
            return;
        }

        AboutUpdateStatusText.Text = update.IsUpdateAvailable
            ? Loc.Tf("About_Update_Available_Format", update.LatestTag)
            : Loc.Tf("About_Update_UpToDate_Format", update.LatestTag);
        AboutArtifactText.Text = string.IsNullOrWhiteSpace(update.ArtifactName)
            ? Loc.T("About_Artifact_NoAsset")
            : Loc.Tf("About_Artifact_Format", update.ArtifactName, FormatHelper.FormatFileSize(update.ArtifactSize));
        AboutUpdateCheckedText.Text = Loc.Tf("About_CheckedAt_Format", FormatHelper.FormatDateTime(update.CheckedAt));
        AboutChangelogText.Text = update.Changelog.Count == 0
            ? Loc.T("About_Changelog_Empty")
            : Loc.T("About_Changelog_Header") + "\n" + string.Join('\n', update.Changelog.Take(8));
    }
}
