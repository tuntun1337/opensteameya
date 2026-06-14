using SteamEyaWinUI.Models;

namespace SteamEyaWinUI.Services;

// 一条 loadout 装备条目（阵营 class、槽位、itemdef）。SO 缓存解析与一键装配读回共用。
internal readonly record struct CsLoadoutEntry(uint ClassId, uint SlotId, uint ItemDefinition);

internal static class CsSoCacheParser
{
    public static List<CsLoadoutEntry> ParseLoadoutFromWelcome(byte[] welcomePayload, uint accountId)
    {
        var entries = new List<CsLoadoutEntry>();
        var reader = new SteamProtoReader(welcomePayload);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (field == 3)
            {
                ParseSoCacheSubscribed(reader.ReadLengthDelimited(wireType), accountId, entries);
            }
            else
            {
                reader.Skip(wireType);
            }
        }

        return entries;
    }

    public static bool TryGetOwnerSoidFromWelcome(byte[] welcomePayload, out uint ownerType, out ulong ownerId)
    {
        ownerType = 0;
        ownerId = 0;
        var reader = new SteamProtoReader(welcomePayload);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (field != 3)
            {
                reader.Skip(wireType);
                continue;
            }

            if (TryGetOwnerSoidFromCacheSubscribed(reader.ReadLengthDelimited(wireType), out ownerType, out ownerId))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryGetSoCacheVersionFromWelcome(byte[] welcomePayload, out ulong version)
    {
        version = 0;
        var reader = new SteamProtoReader(welcomePayload);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (field != 3)
            {
                reader.Skip(wireType);
                continue;
            }

            if (TryGetSoCacheVersionFromCacheSubscribed(reader.ReadLengthDelimited(wireType), out version))
            {
                return true;
            }
        }

        return false;
    }

    public static List<CsLoadoutEntry> ParseLoadoutFromGcMessage(
        uint msgType,
        byte[] payload,
        uint accountId)
    {
        try
        {
            return msgType switch
            {
                CsLoadoutConstants.SoCacheSubscribed =>
                    ParseLoadoutFromSoCacheSubscribed(payload, accountId),
                CsLoadoutConstants.SoUpdateMultiple =>
                    ParseLoadoutFromSoMultipleObjects(payload, accountId),
                CsLoadoutConstants.SoUpdate or CsLoadoutConstants.SoCreate =>
                    ParseLoadoutFromSingleObject(payload, accountId),
                _ => []
            };
        }
        catch
        {
            return [];
        }
    }

    public static bool IsLoadoutSoMessage(uint msgType) =>
        msgType is CsLoadoutConstants.SoCacheSubscribed
            or CsLoadoutConstants.SoUpdateMultiple
            or CsLoadoutConstants.SoUpdate
            or CsLoadoutConstants.SoCreate;

    public static void MergeEntries(
        IDictionary<(uint ClassId, uint SlotId), CsLoadoutEntry> target,
        IEnumerable<CsLoadoutEntry> source)
    {
        foreach (var entry in source)
        {
            target[(entry.ClassId, entry.SlotId)] = entry;
        }
    }

    private static List<CsLoadoutEntry> ParseLoadoutFromSoCacheSubscribed(byte[] payload, uint accountId)
    {
        var entries = new List<CsLoadoutEntry>();
        ParseSoCacheSubscribed(payload, accountId, entries);
        return entries;
    }

    private static bool TryGetOwnerSoidFromCacheSubscribed(byte[] body, out uint ownerType, out ulong ownerId)
    {
        ownerType = 0;
        ownerId = 0;
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (field == 4)
            {
                return TryDecodeOwnerSoid(reader.ReadLengthDelimited(wireType), out ownerType, out ownerId);
            }

            reader.Skip(wireType);
        }

        return false;
    }

    private static bool TryGetSoCacheVersionFromCacheSubscribed(byte[] body, out ulong version)
    {
        version = 0;
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (field == 3)
            {
                version = wireType == 1
                    ? reader.ReadFixed64(wireType)
                    : reader.ReadVarint(wireType);
                return true;
            }

            reader.Skip(wireType);
        }

        return false;
    }

    private static bool TryDecodeOwnerSoid(byte[] body, out uint ownerType, out ulong ownerId)
    {
        ownerType = 0;
        ownerId = 0;
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field)
            {
                case 1:
                    ownerType = (uint)reader.ReadVarint(wireType);
                    break;

                case 2:
                    ownerId = reader.ReadVarint(wireType);
                    break;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        return ownerType != 0 && ownerId != 0;
    }

    private static List<CsLoadoutEntry> ParseLoadoutFromSoMultipleObjects(byte[] body, uint accountId)
    {
        var entries = new List<CsLoadoutEntry>();
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            // CMsgSOMultipleObjects: objects_modified=2, version=3 (fixed64).
            if (field is 2 or 4 or 5)
            {
                if (wireType == 2)
                {
                    TryAddLoadoutEntryFromMultipleObject(
                        reader.ReadLengthDelimited(wireType),
                        accountId,
                        entries);
                }
                else
                {
                    reader.Skip(wireType);
                }
            }
            else
            {
                reader.Skip(wireType);
            }
        }

        return entries;
    }

    private static List<CsLoadoutEntry> ParseLoadoutFromSingleObject(byte[] body, uint accountId)
    {
        var entries = new List<CsLoadoutEntry>();
        TryAddLoadoutEntryFromSingleObject(body, accountId, entries);
        return entries;
    }

    private static void TryAddLoadoutEntryFromMultipleObject(
        byte[] body,
        uint accountId,
        List<CsLoadoutEntry> entries)
    {
        var typeId = 0;
        byte[]? objectData = null;
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field)
            {
                case 1:
                    typeId = (int)reader.ReadVarint(wireType);
                    break;

                case 2:
                    objectData = reader.ReadLengthDelimited(wireType);
                    break;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        TryAppendDecodedEntry(typeId, objectData, accountId, entries);
    }

    private static void TryAddLoadoutEntryFromSingleObject(
        byte[] body,
        uint accountId,
        List<CsLoadoutEntry> entries)
    {
        var typeId = 0;
        byte[]? objectData = null;
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field)
            {
                case 2:
                    typeId = (int)reader.ReadVarint(wireType);
                    break;

                case 3:
                    objectData = reader.ReadLengthDelimited(wireType);
                    break;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        TryAppendDecodedEntry(typeId, objectData, accountId, entries);
    }

    private static void TryAppendDecodedEntry(
        int typeId,
        byte[]? objectData,
        uint accountId,
        List<CsLoadoutEntry> entries)
    {
        if (objectData is null || objectData.Length == 0)
        {
            return;
        }

        if (typeId == CsLoadoutConstants.SoTypeEquipSlot)
        {
            TryDecodeEquipSlotEntry(objectData, accountId, entries);
            return;
        }

        if (typeId != CsLoadoutConstants.SoTypeDefaultEquippedDefinition ||
            !TryDecodeDefaultEquippedDefinition(objectData, out var entry) ||
            entry.AccountId != accountId)
        {
            return;
        }

        TryAddValidatedEntry(entries, entry.ClassId, entry.SlotId, entry.ItemDefinition);
    }

    private static void ParseSoCacheSubscribed(byte[] body, uint accountId, List<CsLoadoutEntry> entries)
    {
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            if (field == 2)
            {
                ParseSubscribedType(reader.ReadLengthDelimited(wireType), accountId, entries);
            }
            else
            {
                reader.Skip(wireType);
            }
        }
    }

    private static void ParseSubscribedType(byte[] body, uint accountId, List<CsLoadoutEntry> entries)
    {
        var typeId = 0;
        var objectDataList = new List<byte[]>();
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field)
            {
                case 1:
                    typeId = (int)reader.ReadVarint(wireType);
                    break;

                case 2:
                    objectDataList.Add(reader.ReadLengthDelimited(wireType));
                    break;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        if (typeId == 0)
        {
            return;
        }

        foreach (var objectData in objectDataList)
        {
            TryAppendDecodedEntry(typeId, objectData, accountId, entries);
        }
    }

    private static bool TryDecodeEquipSlotEntry(
        byte[] body,
        uint accountId,
        List<CsLoadoutEntry> entries)
    {
        if (!TryDecodeEquipSlot(body, out var slot))
        {
            return false;
        }

        if (slot.AccountId != 0 && slot.AccountId != accountId)
        {
            return false;
        }

        var itemDefinition = slot.ItemDefinition;
        if (itemDefinition == 0)
        {
            itemDefinition = TryGetDefaultItemDefinition(slot.ItemId, out var defaultDefinition)
                ? defaultDefinition
                : slot.ItemId is > 0 and <= 10_000
                    ? (uint)slot.ItemId
                    : 0;
        }

        return TryAddValidatedEntry(entries, slot.ClassId, slot.SlotId, itemDefinition);
    }

    private static bool TryGetDefaultItemDefinition(ulong itemId, out uint itemDefinition)
    {
        if ((itemId & CsLoadoutConstants.ItemIdDefaultItemMask) !=
            CsLoadoutConstants.ItemIdDefaultItemMask)
        {
            itemDefinition = 0;
            return false;
        }

        itemDefinition = (uint)(itemId & ~CsLoadoutConstants.ItemIdDefaultItemMask);
        return itemDefinition is > 0 and <= 10_000;
    }

    private static bool TryDecodeEquipSlot(
        byte[] body,
        out (uint AccountId, uint ClassId, uint SlotId, ulong ItemId, uint ItemDefinition) slot)
    {
        slot = default;
        uint accountId = 0;
        uint classId = 0;
        uint slotId = 0;
        ulong itemId = 0;
        uint itemDefinition = 0;
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field)
            {
                case 1:
                    accountId = (uint)reader.ReadVarint(wireType);
                    break;

                case 2:
                    classId = (uint)reader.ReadVarint(wireType);
                    break;

                case 3:
                    slotId = (uint)reader.ReadVarint(wireType);
                    break;

                case 4:
                    itemId = reader.ReadVarint(wireType);
                    break;

                case 5:
                    itemDefinition = (uint)reader.ReadVarint(wireType);
                    break;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        if (classId == 0 || slotId == 0)
        {
            return false;
        }

        slot = (accountId, classId, slotId, itemId, itemDefinition);
        return true;
    }

    private static bool TryAddValidatedEntry(
        List<CsLoadoutEntry> entries,
        uint classId,
        uint slotId,
        uint itemDefinition)
    {
        if (classId is not (CsLoadoutConstants.TeamTerrorist or CsLoadoutConstants.TeamCounterTerrorist))
        {
            return false;
        }

        // 接受全部武器 loadout 槽位：1=近战、2–7=手枪、8–13=中级、14–19=步枪、34=Zeus。
        // （R8 功能的 planner 自身只看副武器槽，放宽这里不影响它，但能让整套配装的读回校验覆盖步枪/微冲。）
        if (slotId is not (1 or (>= 2 and <= 19) or 34))
        {
            return false;
        }

        if (itemDefinition is 0 or > 10_000)
        {
            return false;
        }

        entries.Add(new CsLoadoutEntry(classId, slotId, itemDefinition));
        return true;
    }

    private static bool TryDecodeDefaultEquippedDefinition(
        byte[] body,
        out (uint AccountId, uint ItemDefinition, uint ClassId, uint SlotId) entry)
    {
        entry = default;
        uint accountId = 0;
        uint itemDefinition = 0;
        uint classId = 0;
        uint slotId = 0;
        var reader = new SteamProtoReader(body);

        while (reader.TryReadTag(out var field, out var wireType))
        {
            switch (field)
            {
                case 1:
                    accountId = (uint)reader.ReadVarint(wireType);
                    break;

                case 2:
                    itemDefinition = (uint)reader.ReadVarint(wireType);
                    break;

                case 3:
                    classId = (uint)reader.ReadVarint(wireType);
                    break;

                case 4:
                    slotId = (uint)reader.ReadVarint(wireType);
                    break;

                default:
                    reader.Skip(wireType);
                    break;
            }
        }

        if (accountId == 0 || classId == 0 || slotId == 0 || itemDefinition == 0)
        {
            return false;
        }

        entry = (accountId, itemDefinition, classId, slotId);
        return true;
    }
}
