using System.Text.RegularExpressions;
using Penumbra.GameData.Data;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;

namespace Snappy.Features.Pmp.ChangedItems;

public sealed partial class SnapshotChangedItemService
{
    private static bool TryIdentifyByPathHeuristic(ObjectIdentification identifier, string gamePath,
        IDictionary<string, IIdentifiedObjectData> items)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
            return false;

        var normalized = gamePath.Replace('\\', '/');
        if (TryParseEquipmentLikePath(normalized, 'e', out var equipId, out var slot, out var variant))
        {
            AddEquipItems(identifier, equipId, slot, variant, items);
            return items.Count > 0;
        }

        if (TryParseEquipmentLikePath(normalized, 'a', out var accessoryId, out slot, out variant))
        {
            AddEquipItems(identifier, accessoryId, slot, variant, items);
            return items.Count > 0;
        }

        if (TryParseWeaponPath(normalized, out var weaponId, out var weaponType, out variant))
        {
            AddWeaponItems(identifier, weaponId, weaponType, variant, items);
            return items.Count > 0;
        }

        return false;
    }

    private static void AddEquipItems(ObjectIdentification identifier, ushort primaryId, EquipSlot slot,
        Variant variant, IDictionary<string, IIdentifiedObjectData> items)
    {
        foreach (var item in identifier.Identify((PrimaryId)primaryId, (SecondaryId)0, variant, slot))
            items.UpdateCountOrSet(item.Name, () => new IdentifiedItem(item));
    }

    private static void AddWeaponItems(ObjectIdentification identifier, ushort primaryId, ushort secondaryId,
        Variant variant, IDictionary<string, IIdentifiedObjectData> items)
    {
        foreach (var item in identifier.Identify((PrimaryId)primaryId, (SecondaryId)secondaryId, variant, EquipSlot.MainHand))
            items.UpdateCountOrSet(item.Name, () => new IdentifiedItem(item));
    }

    private static bool TryParseEquipmentLikePath(string path, char prefix, out ushort primaryId, out EquipSlot slot,
        out Variant variant)
    {
        primaryId = 0;
        slot = EquipSlot.Unknown;
        variant = Variant.None;

        var match = prefix == 'e' ? EquipmentFolderRegex.Match(path) : AccessoryFolderRegex.Match(path);
        if (!match.Success || !TryParseUShort(match.Groups["id"].Value, out primaryId))
            return false;

        if (TryParseSlot(path, out var parsedSlot))
            slot = parsedSlot;

        if (TryParseVariant(path, out var parsedVariant))
            variant = parsedVariant;

        return true;
    }

    private static bool TryParseWeaponPath(string path, out ushort primaryId, out ushort secondaryId, out Variant variant)
    {
        primaryId = 0;
        secondaryId = 0;
        variant = Variant.None;

        var match = WeaponFolderRegex.Match(path);
        if (!match.Success || !TryParseUShort(match.Groups["id"].Value, out primaryId))
            return false;

        if (match.Groups["type"].Success)
            TryParseUShort(match.Groups["type"].Value, out secondaryId);

        if (TryParseVariant(path, out var parsedVariant))
            variant = parsedVariant;

        return true;
    }

    private static bool TryParseVariant(string path, out Variant variant)
    {
        variant = Variant.None;
        var match = VariantRegex.Match(path);
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups["variant"].Value, out var value))
            return false;

        if (value is < 0 or > byte.MaxValue)
            return false;

        variant = new Variant((byte)value);
        return true;
    }

    private static bool TryParseSlot(string path, out EquipSlot slot)
    {
        slot = EquipSlot.Unknown;
        var match = SlotRegex.Match(Path.GetFileName(path));
        if (!match.Success)
            return false;

        var suffix = match.Groups["slot"].Value;
        if (!Names.SuffixToEquipSlot.TryGetValue(suffix, out slot))
            return false;

        return slot != EquipSlot.Unknown;
    }

    private static bool TryParseUShort(string value, out ushort result)
        => ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);

    private static readonly Regex EquipmentFolderRegex = new(
        @"chara\/equipment\/e(?<id>\d{4})\/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AccessoryFolderRegex = new(
        @"chara\/accessory\/a(?<id>\d{4})\/",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WeaponFolderRegex = new(
        @"chara\/weapon\/w(?<id>\d{4})\/(?:obj\/body\/b(?<type>\d{4})\/)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VariantRegex = new(
        @"\/v(?<variant>\d{2,4})[\/_]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SlotRegex = new(
        @"_(?<slot>[a-z]{3})(?:_|\.)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
