using System.Globalization;
using SteamEyaWinUI.Localization;
using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

internal sealed class CsLoadoutService
{
    private static readonly TimeSpan EquipSoWaitTimeout = TimeSpan.FromSeconds(8);
    private static readonly SemaphoreSlim EquipGate = new(1, 1);

    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    // 一键装配整套预设：把两阵营各槽位的原版武器（itemdef）一次性写入，读回校验。
    public async Task<CsLoadoutApplyResult> ApplyPresetAsync(
        CsLoadoutPreset preset,
        string refreshToken,
        string steamId,
        CancellationToken cancellationToken = default)
    {
        if (!await EquipGate.WaitAsync(TimeSpan.Zero, cancellationToken))
        {
            throw new InvalidOperationException(Loc.T("Cs_Loadout_Busy"));
        }

        try
        {
            return await ApplyPresetCoreAsync(preset, refreshToken, steamId, cancellationToken);
        }
        finally
        {
            EquipGate.Release();
        }
    }

    private async Task<CsLoadoutApplyResult> ApplyPresetCoreAsync(
        CsLoadoutPreset preset,
        string refreshToken,
        string steamId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!ulong.TryParse(steamId, CultureInfo.InvariantCulture, out var steamId64))
            {
                throw new InvalidOperationException(Loc.T("Cs_Loadout_BadSteam64"));
            }

            var accountId = CsGcSession.GetAccountId(steamId64);

            var requested = new List<(uint Team, uint Slot, uint Def)>();
            foreach (var (slot, def) in preset.T)
            {
                requested.Add((CsLoadoutConstants.TeamTerrorist, slot, def));
            }
            foreach (var (slot, def) in preset.Ct)
            {
                requested.Add((CsLoadoutConstants.TeamCounterTerrorist, slot, def));
            }

            if (requested.Count == 0)
            {
                return new CsLoadoutApplyResult(0, 0, []);
            }

            await using var cmClient = new SteamCmClient(HttpClient);
            await cmClient.ConnectAndLogOnAsync(refreshToken, steamId, cancellationToken);

            try
            {
                await cmClient.SetGamesPlayedAsync([CsGcSession.Cs2AppId], cancellationToken);
                var welcomePayload = await CsGcSession.ConnectAsync(cmClient, cancellationToken);

                // 读当前配装（welcome 内嵌 SO 缓存，已放宽到全槽位），算出与目标的差异。
                var current = new Dictionary<(uint Team, uint Slot), uint>();
                foreach (var entry in CsSoCacheParser.ParseLoadoutFromWelcome(welcomePayload, accountId))
                {
                    current[(entry.ClassId, entry.SlotId)] = entry.ItemDefinition;
                }

                // 已是目标状态的槽位直接计为已确认（GC 不会回写未改动的槽，发了反而误报失败）；只发差异。
                var toSend = new List<(uint Team, uint Slot, uint Def)>();
                var alreadyOk = 0;
                foreach (var item in requested)
                {
                    if (current.TryGetValue((item.Team, item.Slot), out var cur) && cur == item.Def)
                    {
                        alreadyOk++;
                    }
                    else
                    {
                        toSend.Add(item);
                    }
                }

                if (toSend.Count == 0)
                {
                    AppLog.Info($"一键配装：{requested.Count} 个位置已是目标状态，无需改动。");
                    return new CsLoadoutApplyResult(requested.Count, requested.Count, []);
                }

                var tappedMessages = new List<SteamCmClient.SteamGcClientMessage>();
                var tappedMessagesLock = new object();
                cmClient.SetGcMessageTap(message =>
                {
                    lock (tappedMessagesLock)
                    {
                        tappedMessages.Add(message);
                    }
                });

                List<CsLoadoutEntry> verifiedEntries;
                try
                {
                    CsSoCacheParser.TryGetSoCacheVersionFromWelcome(welcomePayload, out var soCacheVersion);
                    var changeNum = BuildChangeNum(soCacheVersion);

                    var slotEntries = toSend
                        .Select(r => (r.Team, r.Slot, CsLoadoutConstants.BuildDefaultBaseItemId(r.Def)))
                        .ToList();

                    await cmClient.SendGcProtobufMessageAsync(
                        CsGcSession.Cs2AppId,
                        CsLoadoutConstants.AdjustEquipSlotsManual,
                        EncodeAdjustEquipSlotsMulti(slotEntries, changeNum),
                        cancellationToken);

                    await WaitForEquipSoUpdateAsync(
                        cmClient,
                        tappedMessages,
                        tappedMessagesLock,
                        accountId,
                        cancellationToken);

                    // 整套配装可能分多条 SO 更新返回，首条到达后再宽限一会，收齐尾随更新再合并。
                    await Task.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken);
                    verifiedEntries = MergeLoadoutFromTappedMessages(tappedMessages, tappedMessagesLock, accountId);
                }
                finally
                {
                    cmClient.SetGcMessageTap(null);
                }

                var verifiedSet = verifiedEntries
                    .Select(e => (e.ClassId, e.SlotId, e.ItemDefinition))
                    .ToHashSet();

                var failures = new List<string>();
                var confirmedChanges = 0;
                foreach (var (team, slot, def) in toSend)
                {
                    if (verifiedSet.Contains((team, slot, def)))
                    {
                        confirmedChanges++;
                    }
                    else
                    {
                        var teamName = team == CsLoadoutConstants.TeamTerrorist ? "T" : "CT";
                        var name = CsWeaponCatalog.ByDef(def)?.LocalizedName ?? def.ToString(CultureInfo.InvariantCulture);
                        failures.Add($"{teamName} #{slot} {name}");
                    }
                }

                var result = new CsLoadoutApplyResult(requested.Count, alreadyOk + confirmedChanges, failures);
                AppLog.Info($"一键配装：请求 {result.Requested}，确认 {result.Confirmed}，失败 {failures.Count}。");
                return result;
            }
            finally
            {
                try
                {
                    await cmClient.SetGamesPlayedAsync([], CancellationToken.None);
                }
                catch
                {
                    // best-effort
                }
            }
        }
        catch (Exception ex)
        {
            if (IsSteamSessionConflict(ex))
            {
                var conflict = new InvalidOperationException(
                    (ex.Data["CmConflict"] as string) == "SessionReplaced"
                        ? ex.Message
                        : Loc.T("Cs_Loadout_CmDisconnected"),
                    ex);
                AppLog.Error($"一键配装失败：{conflict.Message}");
                throw conflict;
            }

            AppLog.Error($"一键配装失败：{ex.Message}");
            throw;
        }
    }

    private static async Task<List<CsLoadoutEntry>> WaitForEquipSoUpdateAsync(
        SteamCmClient cmClient,
        List<SteamCmClient.SteamGcClientMessage> tappedMessages,
        object tappedMessagesLock,
        uint accountId,
        CancellationToken cancellationToken)
    {
        try
        {
            await WaitForSoUpdateMessageAsync(
                cmClient,
                tappedMessages,
                tappedMessagesLock,
                cancellationToken);
        }
        catch (TimeoutException)
        {

        }

        return MergeLoadoutFromTappedMessages(tappedMessages, tappedMessagesLock, accountId);
    }

    private static async Task WaitForSoUpdateMessageAsync(
        SteamCmClient cmClient,
        List<SteamCmClient.SteamGcClientMessage> tappedMessages,
        object tappedMessagesLock,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + EquipSoWaitTimeout;
        var processedCount = 0;
        var waitTypes = new[]
        {
            CsLoadoutConstants.SoUpdateMultiple,
            CsLoadoutConstants.SoCacheSubscribed,
            CsLoadoutConstants.SoUpdate,
            CsLoadoutConstants.SoCreate
        };

        while (DateTimeOffset.UtcNow < deadline)
        {
            List<SteamCmClient.SteamGcClientMessage> pendingMessages;
            lock (tappedMessagesLock)
            {
                pendingMessages = tappedMessages
                    .Skip(processedCount)
                    .ToList();
                processedCount = tappedMessages.Count;
            }

            foreach (var message in pendingMessages)
            {
                if (CsSoCacheParser.IsLoadoutSoMessage(message.MessageType))
                {
                    return;
                }
            }

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var waitSlice = remaining < TimeSpan.FromSeconds(1)
                ? remaining
                : TimeSpan.FromSeconds(1);

            foreach (var msgType in waitTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await cmClient.WaitForGcMessageAsync(
                        CsGcSession.Cs2AppId,
                        msgType,
                        waitSlice,
                        cancellationToken,
                        cacheUnmatched: true);
                    return;
                }
                catch (TimeoutException)
                {

                }
            }
        }

        throw new TimeoutException(Loc.T("Cs_Loadout_SoTimeout"));
    }

    private static List<CsLoadoutEntry> MergeLoadoutFromTappedMessages(
        IReadOnlyList<SteamCmClient.SteamGcClientMessage> tappedMessages,
        object tappedMessagesLock,
        uint accountId)
    {
        var loadoutMap = new Dictionary<(uint ClassId, uint SlotId), CsLoadoutEntry>();
        List<SteamCmClient.SteamGcClientMessage> messages;
        lock (tappedMessagesLock)
        {
            messages = tappedMessages.ToList();
        }

        foreach (var message in messages)
        {
            if (!CsSoCacheParser.IsLoadoutSoMessage(message.MessageType))
            {
                continue;
            }

            CsSoCacheParser.MergeEntries(
                loadoutMap,
                CsSoCacheParser.ParseLoadoutFromGcMessage(message.MessageType, message.Payload, accountId));
        }

        return loadoutMap.Values.ToList();
    }

    // 用 SteamCmClient 打在异常上的语言中立标记判定，不依赖本地化后的 Message 文本。
    private static bool IsSteamSessionConflict(Exception ex) =>
        ex.Data["CmConflict"] is string;

    private static uint BuildChangeNum(ulong soCacheVersion) =>
        soCacheVersion != 0
            ? (uint)((soCacheVersion + 1) & 0xFFFFFFFF)
            : 1;

    // 每槽各自带 itemId 的批量装配消息（整套配装一次发出）。
    private static byte[] EncodeAdjustEquipSlotsMulti(
        IReadOnlyList<(uint ClassId, uint SlotId, ulong ItemId)> slots,
        uint changeNum) =>
        SteamProtoWriter.Build(writer =>
        {
            foreach (var (classId, slotId, itemId) in slots)
            {
                writer.WriteBytes(1, SteamProtoWriter.Build(slotWriter =>
                {
                    slotWriter.WriteUInt32(1, classId);
                    slotWriter.WriteUInt32(2, slotId);
                    slotWriter.WriteUInt64(3, itemId);
                }));
            }

            writer.WriteUInt32(2, changeNum);
        });
}
