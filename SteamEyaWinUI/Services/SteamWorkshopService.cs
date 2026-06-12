using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SteamEyaWinUI.Services;

internal sealed class SteamWorkshopService
{
    private const uint AppId = 730;
    private const int ListType = 1;
    private const int DelayMs = 600;

    private readonly JwtTokenService _jwtTokenService = new();

    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = false
    })
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<int> ClearSubscriptionsAsync(
        string eyaToken,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("正在校验 EYA 令牌...");
        var token = _jwtTokenService.Validate(eyaToken);

        await using var cmClient = new SteamCmClient(HttpClient);

        progress?.Report("正在连接 Steam...");
        await cmClient.ConnectAndLogOnAsync(eyaToken, token.SteamId, cancellationToken);

        progress?.Report($"已登录 Steam (SteamID: {token.SteamId})");
        progress?.Report("正在获取 Web 会话...");
        var cookieHeader = await GetWebSessionAsync(cmClient, eyaToken, token.SteamId, cancellationToken);

        progress?.Report("正在获取已订阅的创意工坊项目列表...");
        var (ids, titles) = await EnumerateSubscriptionsAsync(token.SteamId, cookieHeader, cancellationToken);

        if (ids.Count == 0)
        {
            progress?.Report("没有找到 AppID 730 的创意工坊订阅。");
            return 0;
        }

        progress?.Report($"找到 {ids.Count} 个订阅，正在取消...");

        var unsubscribed = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = ids[i];
            var title = titles.GetValueOrDefault(id, "");
            var result = await cmClient.UnsubscribePublishedFileAsync(
                ulong.Parse(id),
                AppId,
                ListType,
                cancellationToken);

            var gone = result == SteamEresult.Ok &&
                !await cmClient.IsPublishedFileSubscribedAsync(
                    ulong.Parse(id),
                    AppId,
                    ListType,
                    cancellationToken);

            if (gone)
            {
                unsubscribed++;
                progress?.Report($"[{i + 1}/{ids.Count}] {id} {title} 已退订");
            }
            else
            {
                progress?.Report($"[{i + 1}/{ids.Count}] {id} {title} 失败 (EResult={result})");
            }

            if (i < ids.Count - 1)
            {
                await Task.Delay(DelayMs, cancellationToken);
            }
        }

        progress?.Report($"完成：成功 {unsubscribed}，失败 {ids.Count - unsubscribed}。");
        return unsubscribed;
    }

    private static async Task<string> GetWebSessionAsync(
        SteamCmClient cmClient,
        string refreshToken,
        string steamId,
        CancellationToken cancellationToken)
    {
        var accessToken = await cmClient.GenerateAccessTokenForAppAsync(refreshToken, cancellationToken);
        var steamLoginSecure = Uri.EscapeDataString($"{steamId}||{accessToken}");
        var sessionId = RandomHex(12);
        var clientSessionId = RandomHex(8);

        return $"steamLoginSecure={steamLoginSecure}; sessionid={sessionId}; clientsessionid={clientSessionId}";
    }

    private static async Task<(List<string> ids, Dictionary<string, string> titles)> EnumerateSubscriptionsAsync(
        string steamId,
        string cookieHeader,
        CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        var titles = new Dictionary<string, string>();
        var page = 1;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = $"https://steamcommunity.com/profiles/{steamId}/myworkshopfiles/"
                + $"?appid={AppId}&browsefilter=mysubscriptions&numperpage=30&p={page}&l=english";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Cookie", cookieHeader);
            request.Headers.Add("User-Agent", "Mozilla/5.0");

            using var response = await HttpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode is >= HttpStatusCode.Ambiguous and < HttpStatusCode.BadRequest)
            {
                var location = response.Headers.Location?.ToString();
                throw new InvalidOperationException($"被重定向 (cookie 失效?) -> {location}");
            }

            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            foreach (Match match in Regex.Matches(
                html,
                @"filedetails/\?id=(\d+)""[^>]*><div class=""workshopItemPreviewHolder",
                RegexOptions.CultureInvariant))
            {
                var id = match.Groups[1].Value;
                if (!ids.Contains(id))
                {
                    ids.Add(id);
                }
            }

            foreach (Match match in Regex.Matches(
                html,
                @"filedetails/\?id=(\d+)""[^>]*><div class=""workshopItemTitle"">([^<]*)<",
                RegexOptions.CultureInvariant))
            {
                titles[match.Groups[1].Value] = WebUtility.HtmlDecode(match.Groups[2].Value.Trim());
            }

            var totalMatch = Regex.Match(html, @"of ([\d,]+) entries", RegexOptions.CultureInvariant);
            var total = totalMatch.Success
                ? int.Parse(totalMatch.Groups[1].Value.Replace(",", "", StringComparison.Ordinal))
                : ids.Count;

            if (ids.Count >= total || !html.Contains("workshopItemPreviewHolder", StringComparison.Ordinal))
            {
                break;
            }

            page++;
            await Task.Delay(400, cancellationToken);
        }

        return (ids, titles);
    }

    private static string RandomHex(int byteCount)
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(byteCount)).ToLowerInvariant();
    }
}
