using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SteamEyaWinUI.Services;

internal sealed record SteamAccountData(string Token, string User, string SteamId);

internal sealed record SteamUpstreamServer(string Name, string BaseUrl)
{
    public override string ToString() => Name;
}

internal sealed class SteamLicenseClient
{
    private const string KeyDataPath = "/keygetdata?key=";
    private const int HeaderSkipBytes = 8;

    private static readonly HttpClient DefaultHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public static IReadOnlyList<SteamUpstreamServer> Servers { get; } =
    [
        new("奶味", "http://111.170.18.37:9099"),
        new("伊万", "http://70.39.201.195:9099"),
        new("路飞", "http://38.76.193.80:9099")
    ];

    public async Task<SteamAccountData> GetAccountDataAsync(
        string licenseKey,
        SteamUpstreamServer server,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            throw new ArgumentException("请输入卡密。", nameof(licenseKey));
        }

        var url = $"{server.BaseUrl.TrimEnd('/')}{KeyDataPath}{Uri.EscapeDataString(licenseKey.Trim())}";
        using var response = await DefaultHttpClient.GetAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            throw new InvalidOperationException("卡密不合法或上游服务器选择有误。");
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException("卡密有误或上游服务器选择有误。");
        }

        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var json = DecompressKeyDataResponse(raw);
        return ParseAccountJson(json);
    }

    private static string DecompressKeyDataResponse(byte[] raw)
    {
        if (raw.Length <= HeaderSkipBytes)
        {
            throw new InvalidDataException("上游服务器返回内容过短。");
        }

        using var input = new MemoryStream(raw, HeaderSkipBytes, raw.Length - HeaderSkipBytes, writable: false);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static SteamAccountData ParseAccountJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement.ValueKind switch
        {
            JsonValueKind.Array when document.RootElement.GetArrayLength() > 0 =>
                document.RootElement[0],
            JsonValueKind.Array =>
                throw new JsonException("上游 JSON 没有账号数据。"),
            _ => document.RootElement
        };

        return new SteamAccountData(
            Token: RequireString(root, "token"),
            User: RequireString(root, "user"),
            SteamId: RequireString(root, "steamid"));
    }

    private static string RequireString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"上游 JSON 缺少字段：{name}");
        }

        var text = value.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new JsonException($"上游 JSON 字段为空：{name}");
        }

        return text;
    }
}
