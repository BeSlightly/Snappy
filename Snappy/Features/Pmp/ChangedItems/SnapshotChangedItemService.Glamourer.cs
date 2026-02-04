using Glamourer.Api.Enums;
using Newtonsoft.Json.Linq;
using Penumbra.GameData.Data;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Snappy.Common.Utilities;

namespace Snappy.Features.Pmp.ChangedItems;

public sealed partial class SnapshotChangedItemService
{
    public async Task<IReadOnlySet<string>> GetEquippedItemKeysAsync(string? glamourerBase64)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(glamourerBase64))
            return result;

        if (!GlamourerDesignUtil.TryDecodeDesignJson(glamourerBase64, out var design) || design == null)
            return result;

        var equipped = GlamourerEquipmentParser.ExtractEquippedItems(design);
        if (equipped.Count == 0)
            return result;

        var itemData = await _gameDataProvider.GetItemDataAsync();
        foreach (var (slot, itemId) in equipped)
        {
            if (itemId == 0)
                continue;

            if (itemData.TryGetValue(new ItemId(itemId), slot, out var equipItem))
                result.Add(equipItem.Name);
        }

        return result;
    }
}

internal static class GlamourerEquipmentParser
{
    private static readonly Dictionary<string, EquipSlot> SlotNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MainHand"] = EquipSlot.MainHand,
        ["Mainhand"] = EquipSlot.MainHand,
        ["MainWeapon"] = EquipSlot.MainHand,
        ["Primary"] = EquipSlot.MainHand,
        ["Weapon"] = EquipSlot.MainHand,
        ["OffHand"] = EquipSlot.OffHand,
        ["Offhand"] = EquipSlot.OffHand,
        ["Secondary"] = EquipSlot.OffHand,
        ["Shield"] = EquipSlot.OffHand,
        ["Head"] = EquipSlot.Head,
        ["Body"] = EquipSlot.Body,
        ["Hands"] = EquipSlot.Hands,
        ["Legs"] = EquipSlot.Legs,
        ["Feet"] = EquipSlot.Feet,
        ["Ears"] = EquipSlot.Ears,
        ["Earring"] = EquipSlot.Ears,
        ["Neck"] = EquipSlot.Neck,
        ["Necklace"] = EquipSlot.Neck,
        ["Wrists"] = EquipSlot.Wrists,
        ["Bracelets"] = EquipSlot.Wrists,
        ["RFinger"] = EquipSlot.RFinger,
        ["RightFinger"] = EquipSlot.RFinger,
        ["RightRing"] = EquipSlot.RFinger,
        ["RingR"] = EquipSlot.RFinger,
        ["LFinger"] = EquipSlot.LFinger,
        ["LeftFinger"] = EquipSlot.LFinger,
        ["LeftRing"] = EquipSlot.LFinger,
        ["RingL"] = EquipSlot.LFinger,
    };

    private static readonly string[] ItemIdKeys =
    [
        "ItemId",
        "ItemID",
        "Item",
        "Id",
    ];

    public static HashSet<(EquipSlot Slot, uint ItemId)> ExtractEquippedItems(JObject design)
    {
        var results = new HashSet<(EquipSlot Slot, uint ItemId)>();
        WalkToken(design, results);
        return results;
    }

    private static void WalkToken(JToken token, HashSet<(EquipSlot Slot, uint ItemId)> results)
    {
        if (token is JObject obj)
        {
            TryExtractSlotItemFromObject(obj, results);
            foreach (var prop in obj.Properties())
            {
                if (TryGetSlot(prop.Name, out var slot) && TryGetItemId(prop.Value, out var itemId))
                    results.Add((slot, itemId));

                WalkToken(prop.Value, results);
            }
        }
        else if (token is JArray array)
        {
            foreach (var child in array)
                WalkToken(child, results);
        }
    }

    private static void TryExtractSlotItemFromObject(JObject obj, HashSet<(EquipSlot Slot, uint ItemId)> results)
    {
        var slotToken = obj["Slot"] ?? obj["slot"] ?? obj["EquipSlot"] ?? obj["EquipType"];
        if (slotToken == null)
            return;

        if (!TryParseSlotToken(slotToken, out var slot))
            return;

        if (TryGetItemId(obj, out var itemId))
            results.Add((slot, itemId));
    }

    private static bool TryParseSlotToken(JToken token, out EquipSlot slot)
    {
        slot = EquipSlot.Unknown;
        if (token.Type == JTokenType.Integer)
        {
            var value = token.Value<int>();
            if (value is >= 0 and <= byte.MaxValue)
                return TryMapApiSlot((byte)value, out slot);
            return false;
        }

        if (token.Type == JTokenType.String)
            return TryGetSlot(token.Value<string>() ?? string.Empty, out slot);

        return false;
    }

    private static bool TryGetSlot(string name, out EquipSlot slot)
    {
        if (SlotNames.TryGetValue(name, out slot))
            return slot != EquipSlot.Unknown;

        if (byte.TryParse(name, out var numeric))
            return TryMapApiSlot(numeric, out slot);

        slot = EquipSlot.Unknown;
        return false;
    }

    private static bool TryMapApiSlot(byte apiSlot, out EquipSlot slot)
    {
        slot = (ApiEquipSlot)apiSlot switch
        {
            ApiEquipSlot.MainHand => EquipSlot.MainHand,
            ApiEquipSlot.OffHand => EquipSlot.OffHand,
            ApiEquipSlot.Head => EquipSlot.Head,
            ApiEquipSlot.Body => EquipSlot.Body,
            ApiEquipSlot.Hands => EquipSlot.Hands,
            ApiEquipSlot.Legs => EquipSlot.Legs,
            ApiEquipSlot.Feet => EquipSlot.Feet,
            ApiEquipSlot.Ears => EquipSlot.Ears,
            ApiEquipSlot.Neck => EquipSlot.Neck,
            ApiEquipSlot.Wrists => EquipSlot.Wrists,
            ApiEquipSlot.RFinger => EquipSlot.RFinger,
            ApiEquipSlot.LFinger => EquipSlot.LFinger,
            _ => EquipSlot.Unknown,
        };

        return slot != EquipSlot.Unknown;
    }

    private static bool TryGetItemId(JToken token, out uint itemId)
    {
        itemId = 0;
        if (token is JValue value)
        {
            if (value.Type == JTokenType.Integer)
            {
                itemId = value.ToObject<uint>();
                return true;
            }

            if (value.Type == JTokenType.String)
            {
                var text = value.ToString();
                if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId))
                    return true;
            }

            return false;
        }

        if (token is JObject obj)
        {
            foreach (var key in ItemIdKeys)
            {
                if (obj.TryGetValue(key, StringComparison.OrdinalIgnoreCase, out var idToken)
                    && idToken != null)
                {
                    if (idToken.Type == JTokenType.Integer)
                    {
                        itemId = idToken.ToObject<uint>();
                        return true;
                    }

                    if (idToken.Type == JTokenType.String)
                    {
                        var text = idToken.ToString();
                        if (uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out itemId))
                            return true;
                    }
                }
            }

            return false;
        }

        if (token is JArray array)
        {
            if (array.Count > 0 && array[0].Type == JTokenType.Integer)
            {
                itemId = array[0].ToObject<uint>();
                return true;
            }
        }

        return false;
    }

    private static bool TryGetItemId(JObject obj, out uint itemId)
        => TryGetItemId((JToken)obj, out itemId);
}
