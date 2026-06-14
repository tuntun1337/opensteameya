using SteamEyaWinUI.Localization;

namespace SteamEyaWinUI.Services;

internal static class CsGcSession
{
    public const uint Cs2AppId = 730;
    public const uint ClientHello = 4006;
    public const uint ClientWelcome = 4004;
    public const uint CsClientVersion = 2_000_244;

    public static Task<byte[]> ConnectAsync(
        SteamCmClient cmClient,
        CancellationToken cancellationToken)
    {
        return RequestWelcomeAsync(
            cmClient,
            TimeSpan.FromSeconds(45),
            cancellationToken);
    }

    public static async Task<byte[]> RequestWelcomeAsync(
        SteamCmClient cmClient,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var welcomeTask = cmClient.WaitForGcMessageAsync(
                Cs2AppId,
                ClientWelcome,
                TimeSpan.FromSeconds(4),
                cancellationToken);

            await cmClient.SendGcProtobufMessageAsync(
                Cs2AppId,
                ClientHello,
                EncodeClientHello(),
                cancellationToken);

            try
            {
                var message = await welcomeTask;
                return message.Payload;
            }
            catch (TimeoutException)
            {
            
            }
        }

        throw new TimeoutException(Loc.T("Cs_Gc_ConnectTimeout"));
    }

    public static byte[] EncodeClientHello()
    {
        return SteamProtoWriter.Build(writer =>
        {
            writer.WriteUInt32(1, CsClientVersion);
            writer.WriteUInt32(3, 0);
            writer.WriteUInt32(4, 0);
            writer.WriteUInt32(9, 0);
        });
    }

    public static uint GetAccountId(ulong steamId64) =>
        (uint)(steamId64 & 0xFFFFFFFF);
}
