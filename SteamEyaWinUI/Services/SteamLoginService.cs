using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

internal sealed class SteamLoginService
{
    private readonly JwtTokenService _jwtTokenService = new();
    private readonly SteamPathService _steamPathService = new();
    private readonly SteamCryptoService _steamCryptoService = new();
    private readonly SteamConfigService _steamConfigService = new();
    private readonly SteamProcessService _steamProcessService = new();

    public LoginResult Login(string accountName, string eyaToken, IProgress<string>? progress = null)
    {
        progress?.Report("正在校验 EYA 令牌...");
        var token = _jwtTokenService.Validate(eyaToken);

        progress?.Report("正在定位 Steam 安装目录...");
        var paths = _steamPathService.GetSteamPaths();

        progress?.Report("正在加密 EYA 令牌...");
        var encryptedJwt = _steamCryptoService.EncryptToHex(eyaToken, accountName);
        var accountCrc32 = Crc32.ComputeSteamAccountKey(accountName);

        _steamProcessService.EnsureSteamStopped(paths, progress);

        progress?.Report("正在写入 Steam 登录配置...");
        _steamConfigService.UpdateLoginFiles(
            paths,
            accountName,
            token.SteamId,
            encryptedJwt,
            accountCrc32);

        progress?.Report("正在启动 Steam...");
        _steamProcessService.LaunchSteamWithLogin(paths, accountName);

        return new LoginResult(accountName, token.SteamId, token.ExpiresAt);
    }
}
