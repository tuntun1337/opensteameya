using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SteamEyaWinUI.Services;

namespace SteamEyaWinUI.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        AppState.UpdateStateChanged += Render;
        Render();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (AppState.LatestUpdate is null && !AppState.IsCheckingForUpdates)
        {
            _ = AppState.CheckForUpdatesAsync(isAutomatic: true);
        }
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
            AppState.ShowStatus("还没有可下载的更新信息，请先检查更新。", InfoBarSeverity.Warning);
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

        AboutVersionText.Text = $"当前版本：v{update?.CurrentVersion ?? GitHubUpdateService.CurrentVersion}";

        if (isChecking)
        {
            AboutUpdateStatusText.Text = "正在连接 GitHub Releases...";
            AboutUpdateCheckedText.Text = "检查时间：检查中";
            return;
        }

        if (AppState.UpdateCheckError is { } error)
        {
            AboutUpdateStatusText.Text = $"GitHub 连接失败：{error}";
            AboutArtifactText.Text = "最新成品：读取失败";
            AboutUpdateCheckedText.Text = AppState.UpdateCheckedAt.HasValue
                ? $"检查时间：{FormatHelper.FormatDateTime(AppState.UpdateCheckedAt.Value)}"
                : "检查时间：未检查";
            AboutChangelogText.Text = "更新日志：读取失败";
            return;
        }

        if (update is null)
        {
            AboutUpdateStatusText.Text = "启动时会自动检查 GitHub Releases。";
            AboutArtifactText.Text = "最新成品：未检查";
            AboutUpdateCheckedText.Text = "检查时间：未检查";
            AboutChangelogText.Text = "更新日志：未检查";
            return;
        }

        AboutUpdateStatusText.Text = update.IsUpdateAvailable
            ? $"发现新版本 {update.LatestTag}。"
            : $"已是最新版本：{update.LatestTag}。";
        AboutArtifactText.Text = string.IsNullOrWhiteSpace(update.ArtifactName)
            ? "最新成品：未找到下载附件"
            : $"最新成品：{update.ArtifactName}（{FormatHelper.FormatFileSize(update.ArtifactSize)}）";
        AboutUpdateCheckedText.Text = $"检查时间：{FormatHelper.FormatDateTime(update.CheckedAt)}";
        AboutChangelogText.Text = update.Changelog.Count == 0
            ? "更新日志：暂无"
            : "更新日志：\n" + string.Join('\n', update.Changelog.Take(8));
    }
}
