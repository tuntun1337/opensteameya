using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using SteamEyaWinUI.Localization;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

internal sealed class GitHubUpdateService
{
    public const string RepositoryUrl = "https://github.com/tuntun1337/opensteameya";
    public const string ReleasesUrl = $"{RepositoryUrl}/releases";

    private const string LatestReleaseApiUrl = "https://api.github.com/repos/tuntun1337/opensteameya/releases/latest";

    private static readonly HttpClient HttpClient = CreateHttpClient();

    public static string CurrentVersion { get; } = GetCurrentVersion();

    public async Task<GitHubUpdateInfo> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        using var releaseResponse = await HttpClient.GetAsync(LatestReleaseApiUrl, cancellationToken);
        releaseResponse.EnsureSuccessStatusCode();

        await using var releaseStream = await releaseResponse.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync(
            releaseStream,
            GitHubUpdateJsonContext.Default.GitHubReleaseDto,
            cancellationToken)
            ?? throw new InvalidOperationException(Loc.T("Update_EmptyResponse"));

        var metadataAsset = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, "latest.json", StringComparison.OrdinalIgnoreCase));

        GitHubReleaseMetadataDto? metadata = null;
        if (!string.IsNullOrWhiteSpace(metadataAsset?.BrowserDownloadUrl))
        {
            using var metadataResponse = await HttpClient.GetAsync(metadataAsset.BrowserDownloadUrl, cancellationToken);
            metadataResponse.EnsureSuccessStatusCode();

            await using var metadataStream = await metadataResponse.Content.ReadAsStreamAsync(cancellationToken);
            metadata = await JsonSerializer.DeserializeAsync(
                metadataStream,
                GitHubUpdateJsonContext.Default.GitHubReleaseMetadataDto,
                cancellationToken);
        }

        var artifact = FindArtifact(release.Assets, metadata?.ArtifactName);
        var latestVersion = NormalizeVersion(metadata?.Version ?? release.TagName);
        var latestTag = string.IsNullOrWhiteSpace(metadata?.Tag) ? release.TagName : metadata.Tag;
        var currentVersion = CurrentVersion;
        var changelog = metadata?.Changelog?.Where(line => !string.IsNullOrWhiteSpace(line)).ToArray()
            ?? [];

        return new GitHubUpdateInfo(
            currentVersion,
            latestVersion,
            latestTag,
            IsNewerVersion(latestVersion, currentVersion),
            release.HtmlUrl,
            artifact?.Name ?? metadata?.ArtifactName,
            artifact?.BrowserDownloadUrl,
            artifact?.Size ?? metadata?.ArtifactSize,
            metadata?.ArtifactSha256,
            changelog,
            DateTimeOffset.Now);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("SteamEYA-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static GitHubReleaseAssetDto? FindArtifact(
        IReadOnlyList<GitHubReleaseAssetDto> assets,
        string? artifactName)
    {
        if (!string.IsNullOrWhiteSpace(artifactName))
        {
            var exact = assets.FirstOrDefault(asset =>
                string.Equals(asset.Name, artifactName, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        return assets.FirstOrDefault(asset =>
            asset.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
            asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            return NormalizeVersion(informational);
        }

        var version = assembly.GetName().Version;
        return version is null
            ? "0.0.0"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private static bool IsNewerVersion(string latestVersion, string currentVersion)
    {
        return TryParseVersion(latestVersion, out var latest) &&
            TryParseVersion(currentVersion, out var current) &&
            latest.CompareTo(current) > 0;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        version = new Version(0, 0, 0);
        var normalized = NormalizeVersion(value);
        var parts = normalized.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        while (parts.Length < 3)
        {
            parts = [.. parts, "0"];
        }

        return Version.TryParse(string.Join('.', parts.Take(4)), out version!);
    }

    private static string NormalizeVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "0.0.0";
        }

        var normalized = value.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var suffixIndex = normalized.IndexOfAny(['+', '-']);
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        return string.IsNullOrWhiteSpace(normalized) ? "0.0.0" : normalized;
    }

    internal sealed record GitHubReleaseDto(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("assets")] IReadOnlyList<GitHubReleaseAssetDto> Assets);

    internal sealed record GitHubReleaseAssetDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl,
        [property: JsonPropertyName("size")] long Size);

    internal sealed record GitHubReleaseMetadataDto(
        string? Version,
        string? Tag,
        string? ArtifactName,
        long? ArtifactSize,
        string? ArtifactSha256,
        IReadOnlyList<string>? Changelog);
}

// JsonSerializerDefaults.Web 保持旧版反射序列化语义：camelCase + 大小写不敏感，
// latest.json 的字段（version/tag/artifactName...）依赖该命名策略。
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(GitHubUpdateService.GitHubReleaseDto))]
[JsonSerializable(typeof(GitHubUpdateService.GitHubReleaseMetadataDto))]
internal sealed partial class GitHubUpdateJsonContext : JsonSerializerContext;
