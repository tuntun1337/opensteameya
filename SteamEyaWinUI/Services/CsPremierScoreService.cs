using System.Globalization;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

internal sealed class CsPremierScoreService
{
    private const uint Cs2AppId = 730;
    private const uint ClientHello = 4006;
    private const uint ClientWelcome = 4004;
    private const uint ClientRequestPlayersProfile = 9127;
    private const uint PlayersProfile = 9128;
    private const uint PremierRankTypeId = 11;
    private const uint CsClientVersion = 2_000_244;

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task<CsPremierScoreResult> QueryAsync(
        string refreshToken,
        string steamId,
        CancellationToken cancellationToken = default)
    {
        if (!ulong.TryParse(steamId, CultureInfo.InvariantCulture, out var steamId64))
        {
            throw new InvalidOperationException("Steam64 格式不正确，无法查询优先分。");
        }

        var accountId = GetAccountId(steamId64);
        await using var cmClient = new SteamCmClient(HttpClient);
        await cmClient.ConnectAndLogOnAsync(refreshToken, steamId, cancellationToken);

        try
        {
            await cmClient.SetGamesPlayedAsync([Cs2AppId], cancellationToken);
            await ConnectToGameCoordinatorAsync(cmClient, cancellationToken);

            var profileTask = cmClient.WaitForGcMessageAsync(
                Cs2AppId,
                PlayersProfile,
                TimeSpan.FromSeconds(30),
                cancellationToken);

            await cmClient.SendGcProtobufMessageAsync(
                Cs2AppId,
                ClientRequestPlayersProfile,
                EncodePlayersProfileRequest(accountId),
                cancellationToken);

            var profileMessage = await profileTask;
            return DecodePlayersProfile(steamId, accountId, profileMessage.Payload);
        }
        finally
        {
            try
            {
                await cmClient.SetGamesPlayedAsync([], CancellationToken.None);
            }
            catch
            {
                // Best-effort cleanup before logoff.
            }
        }
    }

    private static async Task ConnectToGameCoordinatorAsync(
        SteamCmClient cmClient,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);

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
                await welcomeTask;
                return;
            }
            catch (TimeoutException)
            {
                // The GC often needs more than one hello after launching CS2.
            }
        }

        throw new TimeoutException("连接 CS2 Game Coordinator 超时，暂时无法查询优先分。");
    }

    private static byte[] EncodeClientHello()
    {
        return SteamProtoWriter.Build(writer =>
        {
            writer.WriteUInt32(1, CsClientVersion);
            writer.WriteUInt32(3, 0);
            writer.WriteUInt32(4, 0);
            writer.WriteUInt32(9, 0);
        });
    }

    private static byte[] EncodePlayersProfileRequest(uint accountId)
    {
        return SteamProtoWriter.Build(writer =>
        {
            writer.WriteUInt32(3, accountId);
            writer.WriteUInt32(4, 32);
        });
    }

    private static CsPremierScoreResult DecodePlayersProfile(
        string steamId,
        uint requestedAccountId,
        byte[] body)
    {
        var profiles = new List<CsAccountProfile>();
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field)
            {
                case 2:
                    profiles.Add(DecodeAccountProfile(reader.ReadLengthDelimited(wireType)));
                    break;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        var profile = profiles.FirstOrDefault(value => value.AccountId == requestedAccountId)
            ?? profiles.FirstOrDefault();

        if (profile is null)
        {
            throw new InvalidOperationException("CS2 GC 没有返回账号 Profile。");
        }

        var premier = profile.Rankings.FirstOrDefault(ranking =>
            ranking.RankTypeId == PremierRankTypeId);

        return new CsPremierScoreResult(
            steamId,
            requestedAccountId,
            premier,
            profile.Rankings,
            profile.PenaltySeconds,
            profile.PenaltyReason,
            profile.VacBanned,
            profile.PlayerLevel,
            profile.InMatch);
    }

    private static CsAccountProfile DecodeAccountProfile(byte[] body)
    {
        uint accountId = 0;
        uint penaltySeconds = 0;
        uint penaltyReason = 0;
        var vacBanned = 0;
        int? playerLevel = null;
        var inMatch = false;
        var rankings = new List<CsRankingInfo>();
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field)
            {
                case 1:
                    accountId = (uint)reader.ReadVarint(wireType);
                    break;

                case 2:
                    inMatch = true;
                    reader.Skip(wireType);
                    break;

                case 4:
                    penaltySeconds = (uint)reader.ReadVarint(wireType);
                    break;

                case 5:
                    penaltyReason = (uint)reader.ReadVarint(wireType);
                    break;

                case 6:
                    vacBanned = (int)reader.ReadVarint(wireType);
                    break;

                case 7:
                case 20:
                    rankings.Add(DecodeRanking(reader.ReadLengthDelimited(wireType)));
                    break;

                case 17:
                    playerLevel = (int)reader.ReadVarint(wireType);
                    break;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        return new CsAccountProfile(
            accountId,
            rankings,
            penaltySeconds,
            penaltyReason,
            vacBanned,
            playerLevel,
            inMatch);
    }

    private static CsRankingInfo DecodeRanking(byte[] body)
    {
        uint rankTypeId = 0;
        uint rankId = 0;
        uint wins = 0;
        uint? mapId = null;
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field)
            {
                case 2:
                    rankId = (uint)reader.ReadVarint(wireType);
                    break;

                case 3:
                    wins = (uint)reader.ReadVarint(wireType);
                    break;

                case 6:
                    rankTypeId = (uint)reader.ReadVarint(wireType);
                    break;

                case 13:
                    mapId ??= DecodePerMapRankMapId(reader.ReadLengthDelimited(wireType));
                    break;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        return new CsRankingInfo(rankTypeId, rankId, wins, mapId);
    }

    private static uint? DecodePerMapRankMapId(byte[] body)
    {
        var reader = new SteamProtoReader(body);
        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (field == 1)
            {
                return (uint)reader.ReadVarint(wireType);
            }

            reader.Skip(wireType);
        }

        return null;
    }

    private static uint GetAccountId(ulong steamId64)
    {
        return (uint)(steamId64 & 0xFFFFFFFF);
    }

    private sealed record CsAccountProfile(
        uint AccountId,
        IReadOnlyList<CsRankingInfo> Rankings,
        uint PenaltySeconds,
        uint PenaltyReason,
        int VacBanned,
        int? PlayerLevel,
        bool InMatch);
}
