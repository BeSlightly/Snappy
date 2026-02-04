using Newtonsoft.Json.Linq;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Snappy.Common.Utilities;

namespace Snappy.Features.Pmp.ChangedItems;

public readonly record struct GlamourerCustomizationFilter(
    ModelRace Race,
    Gender Gender,
    CustomizeValue BodyType,
    CustomizeValue Face,
    CustomizeValue Hair,
    CustomizeValue Tail,
    CustomizeValue FacePaint);

public sealed partial class SnapshotChangedItemService
{
    public Task<GlamourerCustomizationFilter?> GetCustomizationFilterAsync(string? glamourerBase64)
    {
        if (string.IsNullOrWhiteSpace(glamourerBase64))
            return Task.FromResult<GlamourerCustomizationFilter?>(null);

        if (!GlamourerDesignUtil.TryDecodeDesignJson(glamourerBase64, out var design) || design == null)
            return Task.FromResult<GlamourerCustomizationFilter?>(null);

        if (!GlamourerCustomizationParser.TryGetCustomizeArray(design, out var customize))
            return Task.FromResult<GlamourerCustomizationFilter?>(null);

        var race = GlamourerCustomizationParser.ResolveModelRace(customize);
        var gender = customize.Gender;
        if (race == ModelRace.Unknown || gender == Gender.Unknown)
            return Task.FromResult<GlamourerCustomizationFilter?>(null);

        var filter = new GlamourerCustomizationFilter(
            race,
            gender,
            customize[CustomizeIndex.BodyType],
            customize[CustomizeIndex.Face],
            customize[CustomizeIndex.Hairstyle],
            customize[CustomizeIndex.TailShape],
            customize[CustomizeIndex.FacePaint]);

        return Task.FromResult<GlamourerCustomizationFilter?>(filter);
    }
}

internal static class GlamourerCustomizationParser
{
    public static bool TryGetCustomizeArray(JObject design, out CustomizeArray customize)
    {
        customize = default;
        if (!TryGetCustomizeObject(design, out var customizeObject))
            return false;

        if (TryParseNonHumanArray(customizeObject, out customize))
            return true;

        var result = new CustomizeArray();
        if (!TryReadIndex(customizeObject, CustomizeIndex.Race, out var race))
            return false;
        if (!TryReadIndex(customizeObject, CustomizeIndex.Gender, out var gender))
            return false;
        if (!TryReadIndex(customizeObject, CustomizeIndex.Clan, out var clan))
            return false;
        if (!TryReadIndex(customizeObject, CustomizeIndex.BodyType, out var bodyType))
            return false;
        if (!TryReadIndex(customizeObject, CustomizeIndex.Face, out var face))
            return false;
        if (!TryReadIndex(customizeObject, CustomizeIndex.Hairstyle, out var hair))
            return false;
        if (!TryReadIndex(customizeObject, CustomizeIndex.TailShape, out var tail))
            return false;
        if (!TryReadIndex(customizeObject, CustomizeIndex.FacePaint, out var facePaint))
            return false;

        result[CustomizeIndex.Race] = (CustomizeValue)race;
        result[CustomizeIndex.Gender] = (CustomizeValue)gender;
        result[CustomizeIndex.Clan] = (CustomizeValue)clan;
        result[CustomizeIndex.BodyType] = (CustomizeValue)bodyType;
        result[CustomizeIndex.Face] = (CustomizeValue)face;
        result[CustomizeIndex.Hairstyle] = (CustomizeValue)hair;
        result[CustomizeIndex.TailShape] = (CustomizeValue)tail;
        result[CustomizeIndex.FacePaint] = (CustomizeValue)facePaint;

        customize = result;
        return true;
    }

    public static ModelRace ResolveModelRace(CustomizeArray customize)
    {
        var clan = customize.Clan;
        if (clan != SubRace.Unknown)
            return clan switch
            {
                SubRace.Midlander => ModelRace.Midlander,
                SubRace.Highlander => ModelRace.Highlander,
                SubRace.Wildwood => ModelRace.Elezen,
                SubRace.Duskwight => ModelRace.Elezen,
                SubRace.Plainsfolk => ModelRace.Lalafell,
                SubRace.Dunesfolk => ModelRace.Lalafell,
                SubRace.SeekerOfTheSun => ModelRace.Miqote,
                SubRace.KeeperOfTheMoon => ModelRace.Miqote,
                SubRace.Seawolf => ModelRace.Roegadyn,
                SubRace.Hellsguard => ModelRace.Roegadyn,
                SubRace.Raen => ModelRace.AuRa,
                SubRace.Xaela => ModelRace.AuRa,
                SubRace.Helion => ModelRace.Hrothgar,
                SubRace.Lost => ModelRace.Hrothgar,
                SubRace.Rava => ModelRace.Viera,
                SubRace.Veena => ModelRace.Viera,
                _ => ModelRace.Unknown
            };

        return customize.Race switch
        {
            Race.Hyur => ModelRace.Midlander,
            Race.Elezen => ModelRace.Elezen,
            Race.Lalafell => ModelRace.Lalafell,
            Race.Miqote => ModelRace.Miqote,
            Race.Roegadyn => ModelRace.Roegadyn,
            Race.AuRa => ModelRace.AuRa,
            Race.Hrothgar => ModelRace.Hrothgar,
            Race.Viera => ModelRace.Viera,
            _ => ModelRace.Unknown,
        };
    }

    private static bool TryGetCustomizeObject(JObject design, out JObject customizeObject)
    {
        customizeObject = null!;
        if (!design.TryGetValue("Customize", StringComparison.OrdinalIgnoreCase, out var token))
            return false;

        if (token is not JObject obj || obj.Count == 0)
            return false;

        customizeObject = obj;
        return true;
    }

    private static bool TryParseNonHumanArray(JObject customizeObject, out CustomizeArray customize)
    {
        customize = default;
        if (!customizeObject.TryGetValue("Array", StringComparison.OrdinalIgnoreCase, out var arrayToken))
            return false;

        if (arrayToken.Type != JTokenType.String)
            return false;

        var base64 = arrayToken.Value<string>();
        if (string.IsNullOrWhiteSpace(base64))
            return false;

        var array = new CustomizeArray();
        if (!array.LoadBase64(base64))
            return false;

        customize = array;
        return true;
    }

    private static bool TryReadIndex(JObject customizeObject, CustomizeIndex index, out byte value)
    {
        value = 0;
        if (!customizeObject.TryGetValue(index.ToString(), StringComparison.OrdinalIgnoreCase, out var token))
            return false;

        if (token is not JObject obj)
            return false;

        if (!obj.TryGetValue("Value", StringComparison.OrdinalIgnoreCase, out var valueToken) || valueToken == null)
            return false;

        return TryReadByte(valueToken, out value);
    }

    private static bool TryReadByte(JToken token, out byte value)
    {
        value = 0;
        if (token.Type == JTokenType.Integer)
        {
            var number = token.Value<int>();
            if (number is < 0 or > byte.MaxValue)
                return false;
            value = (byte)number;
            return true;
        }

        if (token.Type == JTokenType.String)
        {
            var text = token.Value<string>();
            return byte.TryParse(text, out value);
        }

        return false;
    }
}
