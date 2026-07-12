using Luna;
using Penumbra.GameData.Data;
using Penumbra.GameData.Enums;
using Penumbra.GameData.Structs;
using Penumbra.Meta.Manipulations;
using Penumbra.UI.Classes;
using Snappy.Features.Pmp;
using Snappy.Services;

namespace Snappy.Features.Pmp.ChangedItems;

public sealed partial class SnapshotChangedItemService : ISnapshotChangedItemService, IDisposable
{
    private const string UnidentifiedKey = "Misc / Unknown Files";
    private const string CustomizationPrefix = "Customization:";
    private const string FaceDecalPrefix = "Customization: Face Decal ";
    private static readonly HashSet<ChangedItemIconFlag> AlwaysIncludeCategories = BuildAlwaysIncludeCategories();
    private readonly PenumbraGameDataProvider _gameDataProvider;

    public SnapshotChangedItemService(LunaLogger log)
    {
        _gameDataProvider = new PenumbraGameDataProvider(log);
    }

    public async Task<SnapshotChangedItemSet> BuildChangedItemsAsync(IEnumerable<string> gamePaths,
        string? base64Manipulations, IReadOnlyDictionary<string, string>? resolvedFileMap = null,
        string? filesDirectory = null)
    {
        var identifier = await _gameDataProvider.GetIdentifierAsync();
        var items = new Dictionary<string, IIdentifiedObjectData>(StringComparer.OrdinalIgnoreCase);
        var gamePathsByItem = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var identifiedByPath = new Dictionary<string, Dictionary<string, IIdentifiedObjectData>>(
            StringComparer.OrdinalIgnoreCase);
        var unresolvedPaths = new List<string>();

        foreach (var gamePath in gamePaths)
        {
            if (string.IsNullOrWhiteSpace(gamePath))
                continue;

            var perPath = new Dictionary<string, IIdentifiedObjectData>(StringComparer.OrdinalIgnoreCase);
            identifier.Identify(perPath, gamePath);
            if (perPath.Count == 0)
            {
                if (!TryIdentifyByPathHeuristic(identifier, gamePath, perPath))
                {
                    unresolvedPaths.Add(gamePath);
                    continue;
                }
            }

            identifiedByPath[NormalizeGamePath(gamePath)] = perPath;
            MergeIdentifiedItems(perPath, items, gamePathsByItem, gamePath);
        }

        ResolveUnidentifiedPaths(unresolvedPaths, identifiedByPath, resolvedFileMap, filesDirectory, items,
            gamePathsByItem);
        AddManipulationItems(identifier, base64Manipulations, items, gamePathsByItem);
        var categories = BuildCategories(items);
        return new SnapshotChangedItemSet(categories, gamePathsByItem);
    }

    public async Task<string> FilterManipulationsAsync(string base64Manipulations, IReadOnlySet<string> selectedItemKeys)
    {
        if (string.IsNullOrEmpty(base64Manipulations) || selectedItemKeys.Count == 0)
            return string.Empty;

        if (!PenumbraMetaCodec.TryParse(base64Manipulations, out var manips) || manips == null)
            return base64Manipulations;

        if (manips.Count == 0)
            return string.Empty;

        var identifier = await _gameDataProvider.GetIdentifierAsync();
        var selectedKeys = BuildSelectionKeysWithAlternateFaces(selectedItemKeys);
        var filtered = new MetaDictionary();

        FilterManipulationEntries(manips.Imc, filtered, identifier, selectedKeys,
            static (dict, id, entry) => dict.TryAdd(id, entry));
        FilterManipulationEntries(manips.Eqp, filtered, identifier, selectedKeys,
            static (dict, id, entry) => dict.TryAdd(id, entry));
        FilterManipulationEntries(manips.Eqdp, filtered, identifier, selectedKeys,
            static (dict, id, entry) => dict.TryAdd(id, entry));
        FilterManipulationEntries(manips.Est, filtered, identifier, selectedKeys,
            static (dict, id, entry) => dict.TryAdd(id, entry));
        FilterManipulationEntries(manips.Gmp, filtered, identifier, selectedKeys,
            static (dict, id, entry) => dict.TryAdd(id, entry));
        FilterManipulationEntries(manips.Rsp, filtered, identifier, selectedKeys,
            static (dict, id, entry) => dict.TryAdd(id, entry));
        FilterManipulationEntries(manips.Atch, filtered, identifier, selectedKeys,
            static (dict, id, entry) => dict.TryAdd(id, entry));
        FilterManipulationEntries(manips.Shp, filtered, identifier, selectedKeys,
            static (dict, id, entry) => dict.TryAdd(id, entry));
        FilterManipulationEntries(manips.Atr, filtered, identifier, selectedKeys,
            static (dict, id, entry) => dict.TryAdd(id, entry));
        FilterManipulationIdentifiers(manips.GlobalEqp, filtered, identifier, selectedKeys,
            static (dict, id) => dict.TryAdd(id));

        return filtered.Count == 0 ? string.Empty : PenumbraMetaCodec.CompressV1(filtered);
    }

    public async Task<IReadOnlySet<string>> GetCustomizationKeysFromManipulationsAsync(string? base64Manipulations,
        GlamourerCustomizationFilter? customizationFilter = null)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(base64Manipulations))
            return keys;

        if (!PenumbraMetaCodec.TryParse(base64Manipulations, out var manips) || manips == null || manips.Count == 0)
            return keys;

        var identifier = await _gameDataProvider.GetIdentifierAsync();
        var items = new Dictionary<string, IIdentifiedObjectData>(StringComparer.OrdinalIgnoreCase);
        foreach (var identifierKey in manips.Est.Keys)
        {
            if (customizationFilter != null && !MatchesEstCustomization(identifierKey, customizationFilter.Value))
                continue;

            identifierKey.AddChangedItems(identifier, items);
        }

        foreach (var (key, data) in items)
        {
            if (ChangedItemFlagExtensions.ToFlag(data.Icon) == ChangedItemIconFlag.Customization)
                keys.Add(key);
        }

        return keys;
    }

    public void Dispose()
    {
        _gameDataProvider.Dispose();
    }

    private static IReadOnlyList<SnapshotChangedItemCategoryGroup> BuildCategories(
        Dictionary<string, IIdentifiedObjectData> items)
    {
        var byCategory = new Dictionary<ChangedItemIconFlag, List<SnapshotChangedItem>>();
        foreach (var (key, data) in items)
        {
            var category = ChangedItemFlagExtensions.ToFlag(data.Icon);
            if (!byCategory.TryGetValue(category, out var list))
            {
                list = new List<SnapshotChangedItem>();
                byCategory[category] = list;
            }

            list.Add(new SnapshotChangedItem(
                key,
                data.ToName(key),
                data.AdditionalData,
                data.Count,
                category));
        }

        var ordered = new List<SnapshotChangedItemCategoryGroup>(ChangedItemFlagExtensions.Order.Count);
        foreach (var category in ChangedItemFlagExtensions.Order)
        {
            byCategory.TryGetValue(category, out var list);
            list ??= new List<SnapshotChangedItem>();
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            ordered.Add(new SnapshotChangedItemCategoryGroup(category, list));
        }

        return ordered;
    }

    private static void AddManipulationItems(ObjectIdentification identifier, string? base64Manipulations,
        Dictionary<string, IIdentifiedObjectData> items,
        Dictionary<string, HashSet<string>> gamePathsByItem)
    {
        if (string.IsNullOrEmpty(base64Manipulations))
            return;

        if (!PenumbraMetaCodec.TryParse(base64Manipulations, out var manips) || manips == null || manips.Count == 0)
            return;

        foreach (var id in EnumerateManipulationIdentifiers(manips))
            AddItemsFromIdentifier(identifier, id, items, gamePathsByItem);
    }

    private static void AddItemsFromIdentifier(ObjectIdentification identifier, IMetaIdentifier id,
        Dictionary<string, IIdentifiedObjectData> items,
        Dictionary<string, HashSet<string>> gamePathsByItem)
    {
        var identified = new Dictionary<string, IIdentifiedObjectData>(StringComparer.OrdinalIgnoreCase);
        AddChangedItemsFromMetaIdentifier(identifier, id, identified);
        if (identified.Count == 0)
            identified[UnidentifiedKey] = new IdentifiedName();

        MergeIdentifiedItems(identified, items, gamePathsByItem, null);
    }

    private static void MergeIdentifiedItems(Dictionary<string, IIdentifiedObjectData> source,
        Dictionary<string, IIdentifiedObjectData> target,
        Dictionary<string, HashSet<string>> gamePathsByItem,
        string? gamePath)
    {
        foreach (var (key, data) in source)
        {
            if (target.TryGetValue(key, out var existing))
                existing.Count += data.Count;
            else
                target[key] = data;

            if (!gamePathsByItem.TryGetValue(key, out var paths))
            {
                paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                gamePathsByItem[key] = paths;
            }

            if (!string.IsNullOrEmpty(gamePath))
                paths.Add(gamePath);
        }
    }

    private static void ResolveUnidentifiedPaths(IReadOnlyList<string> unresolvedPaths,
        IReadOnlyDictionary<string, Dictionary<string, IIdentifiedObjectData>> identifiedByPath,
        IReadOnlyDictionary<string, string>? resolvedFileMap, string? filesDirectory,
        Dictionary<string, IIdentifiedObjectData> items, Dictionary<string, HashSet<string>> gamePathsByItem)
    {
        if (unresolvedPaths.Count == 0)
            return;

        var mappedByDependencyPath = BuildDependencyItemMap(unresolvedPaths, identifiedByPath, resolvedFileMap,
            filesDirectory);

        foreach (var gamePath in unresolvedPaths)
        {
            var normalizedPath = NormalizeGamePath(gamePath);
            if (!string.IsNullOrEmpty(normalizedPath)
                && mappedByDependencyPath.TryGetValue(normalizedPath, out var mappedItems)
                && mappedItems.Count > 0)
            {
                AddMappedItemsForPath(gamePath, mappedItems, items, gamePathsByItem);
                continue;
            }

            AddUnknownItemForPath(gamePath, items, gamePathsByItem);
        }
    }

    private static Dictionary<string, Dictionary<string, IIdentifiedObjectData>> BuildDependencyItemMap(
        IReadOnlyList<string> unresolvedPaths,
        IReadOnlyDictionary<string, Dictionary<string, IIdentifiedObjectData>> identifiedByPath,
        IReadOnlyDictionary<string, string>? resolvedFileMap,
        string? filesDirectory)
    {
        var mapped = new Dictionary<string, Dictionary<string, IIdentifiedObjectData>>(StringComparer.OrdinalIgnoreCase);
        if (resolvedFileMap == null || resolvedFileMap.Count == 0 || string.IsNullOrWhiteSpace(filesDirectory)
            || !Directory.Exists(filesDirectory))
            return mapped;

        var unresolvedLookup = unresolvedPaths
            .Select(NormalizeGamePath)
            .Where(path => !string.IsNullOrEmpty(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (unresolvedLookup.Count == 0)
            return mapped;

        var dependencyGraph = PmpFileDependencyGraph.Build(resolvedFileMap, filesDirectory,
            unresolvedPaths.Concat(identifiedByPath.Keys));
        foreach (var (identifiedPath, identifiedItems) in identifiedByPath)
        {
            if (identifiedItems.Count == 0)
                continue;

            foreach (var dependencyPath in dependencyGraph.ExpandDependencies([identifiedPath]))
            {
                if (!unresolvedLookup.Contains(dependencyPath))
                    continue;

                if (!mapped.TryGetValue(dependencyPath, out var perDependencyItems))
                {
                    perDependencyItems = new Dictionary<string, IIdentifiedObjectData>(StringComparer.OrdinalIgnoreCase);
                    mapped[dependencyPath] = perDependencyItems;
                }

                foreach (var (key, data) in identifiedItems)
                    if (!perDependencyItems.ContainsKey(key))
                        perDependencyItems[key] = CloneWithSingleCount(data);
            }
        }

        return mapped;
    }

    private static string NormalizeGamePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        var normalized = path.Replace('\\', '/').Trim();
        while (normalized.StartsWith("/", StringComparison.Ordinal))
            normalized = normalized[1..];

        return normalized;
    }

    private static IIdentifiedObjectData CloneWithSingleCount(IIdentifiedObjectData source)
    {
        return source switch
        {
            IdentifiedItem item => new IdentifiedItem(item.Item, 1),
            IdentifiedCustomization customization => new IdentifiedCustomization(1)
            {
                Race = customization.Race,
                Gender = customization.Gender,
                Type = customization.Type,
                Value = customization.Value,
            },
            IdentifiedModel model => new IdentifiedModel(model.Model, 1),
            IdentifiedAction action => new IdentifiedAction(action.Action, 1),
            IdentifiedEmote emote => new IdentifiedEmote(emote.Emote, 1),
            IdentifiedCounter => new IdentifiedCounter(1),
            _ => new IdentifiedName(1),
        };
    }

    private static void AddMappedItemsForPath(string gamePath, IReadOnlyDictionary<string, IIdentifiedObjectData> mappedItems,
        Dictionary<string, IIdentifiedObjectData> items, Dictionary<string, HashSet<string>> gamePathsByItem)
    {
        foreach (var (key, mappedItem) in mappedItems)
        {
            if (items.TryGetValue(key, out var existing))
                existing.Count += 1;
            else
                items[key] = CloneWithSingleCount(mappedItem);

            if (!gamePathsByItem.TryGetValue(key, out var mappedPaths))
            {
                mappedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                gamePathsByItem[key] = mappedPaths;
            }

            mappedPaths.Add(gamePath);
        }
    }

    private static void AddUnknownItemForPath(string gamePath, Dictionary<string, IIdentifiedObjectData> items,
        Dictionary<string, HashSet<string>> gamePathsByItem)
    {
        if (items.TryGetValue(UnidentifiedKey, out var existing))
            existing.Count += 1;
        else
            items[UnidentifiedKey] = new IdentifiedName();

        if (!gamePathsByItem.TryGetValue(UnidentifiedKey, out var paths))
        {
            paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            gamePathsByItem[UnidentifiedKey] = paths;
        }

        paths.Add(gamePath);
    }

    private static bool MatchesSelection(ObjectIdentification identifier, HashSet<string> selectedKeys, IMetaIdentifier id)
    {
        var items = new Dictionary<string, IIdentifiedObjectData>(StringComparer.OrdinalIgnoreCase);
        AddChangedItemsFromMetaIdentifier(identifier, id, items);
        if (items.Count == 0)
            return selectedKeys.Contains(UnidentifiedKey);

        return items.Keys.Any(selectedKeys.Contains);
    }

    private static void AddChangedItemsFromMetaIdentifier(ObjectIdentification identifier, IMetaIdentifier id,
        IDictionary<string, IIdentifiedObjectData> items)
    {
        // IMC manipulations can target many variants that do not resolve cleanly when identified via a single
        // variant material path. Resolve by set/slot across variants so they map back to concrete equipment items.
        if (id is ImcIdentifier imc)
        {
            imc.AddChangedItems(identifier, items, allVariants: true);
            return;
        }

        id.AddChangedItems(identifier, items);
    }

    private static IEnumerable<IMetaIdentifier> EnumerateManipulationIdentifiers(MetaDictionary manips)
    {
        foreach (var id in manips.Imc.Keys)
            yield return id;
        foreach (var id in manips.Eqp.Keys)
            yield return id;
        foreach (var id in manips.Eqdp.Keys)
            yield return id;
        foreach (var id in manips.Est.Keys)
            yield return id;
        foreach (var id in manips.Gmp.Keys)
            yield return id;
        foreach (var id in manips.Rsp.Keys)
            yield return id;
        foreach (var id in manips.Atch.Keys)
            yield return id;
        foreach (var id in manips.Shp.Keys)
            yield return id;
        foreach (var id in manips.Atr.Keys)
            yield return id;
        foreach (var id in manips.GlobalEqp)
            yield return id;
    }

    private static void FilterManipulationEntries<TIdentifier, TEntry>(
        IEnumerable<KeyValuePair<TIdentifier, TEntry>> entries,
        MetaDictionary filtered,
        ObjectIdentification identifier,
        HashSet<string> selectedKeys,
        Func<MetaDictionary, TIdentifier, TEntry, bool> tryAdd)
        where TIdentifier : IMetaIdentifier
    {
        foreach (var (id, entry) in entries)
            if (MatchesSelection(identifier, selectedKeys, id))
                tryAdd(filtered, id, entry);
    }

    private static void FilterManipulationIdentifiers<TIdentifier>(
        IEnumerable<TIdentifier> identifiers,
        MetaDictionary filtered,
        ObjectIdentification identifier,
        HashSet<string> selectedKeys,
        Func<MetaDictionary, TIdentifier, bool> tryAdd)
        where TIdentifier : IMetaIdentifier
    {
        foreach (var id in identifiers)
            if (MatchesSelection(identifier, selectedKeys, id))
                tryAdd(filtered, id);
    }

    public SnapshotChangedItemSet FilterToItemKeys(SnapshotChangedItemSet items, IReadOnlySet<string> allowedKeys,
        GlamourerCustomizationFilter? customizationFilter, IReadOnlySet<string>? customizationOverrides)
    {
        if (allowedKeys.Count == 0 && customizationFilter == null && (customizationOverrides == null
                                                                       || customizationOverrides.Count == 0))
            return items;

        var customizationOverrideKeys = BuildCustomizationOverrideKeys(customizationOverrides);
        var customizationPrefixes = BuildCustomizationPathPrefixes(customizationFilter);
        var customizationPathMatches = BuildCustomizationPathMatches(items.GamePathsByItemKey, customizationPrefixes);
        var filteredCategories = new List<SnapshotChangedItemCategoryGroup>();
        var allowedItemKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in items.Categories)
        {
            SnapshotChangedItem[] filteredItems;
            if (category.Category == ChangedItemIconFlag.Customization)
            {
                if (customizationFilter == null && (customizationOverrides == null || customizationOverrides.Count == 0))
                {
                    filteredItems = category.Items.ToArray();
                }
                else
                {
                    filteredItems = category.Items
                        .Where(item => MatchesCustomizationItem(item.Key, customizationFilter, customizationOverrides,
                                          customizationOverrideKeys)
                                      || customizationPathMatches.Contains(item.Key))
                        .ToArray();
                }
            }
            else if (AlwaysIncludeCategories.Contains(category.Category))
            {
                filteredItems = category.Items.ToArray();
            }
            else
            {
                filteredItems = category.Items
                    .Where(item => allowedKeys.Contains(item.Key))
                    .ToArray();
            }

            if (filteredItems.Length == 0)
                continue;

            foreach (var item in filteredItems)
                allowedItemKeys.Add(item.Key);

            filteredCategories.Add(new SnapshotChangedItemCategoryGroup(category.Category, filteredItems));
        }

        var filteredGamePaths = items.GamePathsByItemKey
            .Where(kvp => allowedItemKeys.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

        return new SnapshotChangedItemSet(filteredCategories, filteredGamePaths);
    }

    private static HashSet<ChangedItemIconFlag> BuildAlwaysIncludeCategories()
    {
        return new HashSet<ChangedItemIconFlag>
        {
            ChangedItemIconFlag.Action,
            ChangedItemIconFlag.Emote,
            ChangedItemIconFlag.Monster,
            ChangedItemIconFlag.Demihuman,
            ChangedItemIconFlag.Unknown,
        };
    }

    private static string BuildSkinKey(ModelRace race, Gender gender)
    {
        var raceString = race != ModelRace.Unknown ? $"{race.ToName()} " : string.Empty;
        var genderString = gender != Gender.Unknown ? $"{gender.ToName()} " : "Player ";
        return $"Customization: {raceString}{genderString}Skin Textures";
    }

    private static bool MatchesCustomizationItem(string key, GlamourerCustomizationFilter? filter,
        IReadOnlySet<string>? overrides, HashSet<CustomizationKey>? overrideKeys)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (overrides is { Count: > 0 } && overrides.Contains(key))
            return true;

        if (overrideKeys is { Count: > 0 } && TryParseCustomizationKey(key, out var parsedOverride)
            && overrideKeys.Contains(parsedOverride))
            return true;

        if (filter == null)
            return false;

        if (string.Equals(key, "Customization: All Eyes (Catchlight)", StringComparison.OrdinalIgnoreCase))
            return true;

        if (string.Equals(key, BuildSkinKey(filter.Value.Race, filter.Value.Gender),
                StringComparison.OrdinalIgnoreCase))
            return true;

        if (key.StartsWith(FaceDecalPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (filter.Value.FacePaint.Value <= 0)
                return false;

            return TryParseFaceDecalId(key, out var decalId)
                   && decalId == filter.Value.FacePaint.Value;
        }

        if (!key.StartsWith(CustomizationPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var raceName = filter.Value.Race.ToName();
        var genderName = filter.Value.Gender.ToName();
        if (!key.Contains(raceName, StringComparison.OrdinalIgnoreCase)
            || !key.Contains(genderName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!TryParseCustomizationSlotTextAndId(key, out var slotText, out var id))
            return false;

        foreach (var token in slotText.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var normalized = token.Trim('(', ')');
            if (normalized.Equals("Hair", StringComparison.OrdinalIgnoreCase))
                return id == filter.Value.Hair.Value;
            if (normalized.Equals("Face", StringComparison.OrdinalIgnoreCase))
                return MatchesFaceId(id, filter.Value.Face);
            if (normalized.Equals("Tail", StringComparison.OrdinalIgnoreCase))
                return id == filter.Value.Tail.Value;
            if (normalized.Equals("Body", StringComparison.OrdinalIgnoreCase))
                return id == filter.Value.BodyType.Value;
        }

        return false;
    }

    private static HashSet<CustomizationKey>? BuildCustomizationOverrideKeys(IReadOnlySet<string>? overrides)
    {
        if (overrides == null || overrides.Count == 0)
            return null;

        var keys = new HashSet<CustomizationKey>();
        foreach (var key in overrides)
            if (TryParseCustomizationKey(key, out var parsed))
                keys.Add(parsed);

        return keys.Count == 0 ? null : keys;
    }

    private static HashSet<string> BuildSelectionKeysWithAlternateFaces(IReadOnlySet<string> selectedItemKeys)
    {
        var expanded = new HashSet<string>(selectedItemKeys, StringComparer.OrdinalIgnoreCase);
        foreach (var key in selectedItemKeys)
        {
            if (!TryParseCustomizationKey(key, out var parsed) || parsed.Slot != CustomizationSlot.Face)
                continue;

            if (!TryGetAlternateFaceId(parsed.Id, out var alternateId))
                continue;

            var alternateKey = ReplaceCustomizationKeyId(key, alternateId);
            if (!string.IsNullOrWhiteSpace(alternateKey))
                expanded.Add(alternateKey);
        }

        return expanded;
    }

    private static bool TryParseCustomizationKey(string key, out CustomizationKey customization)
    {
        customization = default;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (TryParseFaceDecalId(key, out var decalId))
        {
            customization = new CustomizationKey(CustomizationSlot.FaceDecal, decalId);
            return true;
        }

        if (!TryParseCustomizationSlotTextAndId(key, out var slotText, out var id))
            return false;
        if (slotText.IndexOf("Hair", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            customization = new CustomizationKey(CustomizationSlot.Hair, id);
            return true;
        }

        if (slotText.IndexOf("Face", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            customization = new CustomizationKey(CustomizationSlot.Face, id);
            return true;
        }

        if (slotText.IndexOf("Tail", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            customization = new CustomizationKey(CustomizationSlot.Tail, id);
            return true;
        }

        if (slotText.IndexOf("Ear", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            customization = new CustomizationKey(CustomizationSlot.Ear, id);
            return true;
        }

        if (slotText.IndexOf("Body", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            customization = new CustomizationKey(CustomizationSlot.Body, id);
            return true;
        }

        return false;
    }

    private static bool TryParseFaceDecalId(string key, out uint decalId)
    {
        decalId = 0;
        if (!key.StartsWith(FaceDecalPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var decalText = key[FaceDecalPrefix.Length..].Trim();
        return uint.TryParse(decalText, NumberStyles.Integer, CultureInfo.InvariantCulture, out decalId);
    }

    private static bool TryParseCustomizationSlotTextAndId(string key, out string slotText, out uint id)
    {
        slotText = string.Empty;
        id = 0;

        if (!key.StartsWith(CustomizationPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var bodyText = key[CustomizationPrefix.Length..].Trim();
        var lastSpace = bodyText.LastIndexOf(' ');
        if (lastSpace < 0 || lastSpace == bodyText.Length - 1)
            return false;

        var idText = bodyText[(lastSpace + 1)..];
        if (!uint.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out id))
            return false;

        slotText = bodyText[..lastSpace];
        return true;
    }

    private enum CustomizationSlot
    {
        Hair,
        Face,
        Tail,
        Ear,
        Body,
        FaceDecal,
    }

    private readonly record struct CustomizationKey(CustomizationSlot Slot, uint Id);

    private static bool MatchesFaceId(uint id, CustomizeValue faceValue)
    {
        var face = faceValue.Value;
        if (id == face)
            return true;

        if (face == 0)
            return false;

        var adjusted = (uint)(face + 100);
        return id == adjusted;
    }

    private static bool TryGetAlternateFaceId(uint id, out uint alternateId)
    {
        if (id < 100)
        {
            alternateId = id + 100;
            return true;
        }

        else if (id <= 110)
        {
            alternateId = id - 100;
            return true;
        }

        alternateId = 0;
        return false;
    }

    private static string? ReplaceCustomizationKeyId(string key, uint id)
    {
        var lastSpace = key.LastIndexOf(' ');
        if (lastSpace < 0 || lastSpace == key.Length - 1)
            return null;

        return string.Concat(key.AsSpan(0, lastSpace + 1),
            id.ToString(CultureInfo.InvariantCulture));
    }

    private static IReadOnlyList<string> BuildCustomizationPathPrefixes(GlamourerCustomizationFilter? filter)
    {
        if (filter == null)
            return Array.Empty<string>();

        var prefixes = new List<string>();
        var hair = filter.Value.Hair.Value;
        var face = filter.Value.Face.Value;
        var tail = filter.Value.Tail.Value;
        var body = filter.Value.BodyType.Value;

        if (hair > 0)
            prefixes.Add($"/obj/hair/h{hair:D4}/");
        if (face > 0)
        {
            prefixes.Add($"/obj/face/f{face:D4}/");
            var adjustedFace = face + 100;
            if (adjustedFace != face)
                prefixes.Add($"/obj/face/f{adjustedFace:D4}/");
        }
        if (tail > 0)
            prefixes.Add($"/obj/tail/t{tail:D4}/");
        if (body > 0)
            prefixes.Add($"/obj/body/b{body:D4}/");

        return prefixes;
    }

    private static HashSet<string> BuildCustomizationPathMatches(
        IReadOnlyDictionary<string, HashSet<string>> gamePathsByItemKey,
        IReadOnlyList<string> prefixes)
    {
        var matches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (prefixes.Count == 0 || gamePathsByItemKey.Count == 0)
            return matches;

        foreach (var (key, itemPaths) in gamePathsByItemKey)
        {
            if (itemPaths == null || itemPaths.Count == 0)
                continue;

            var matched = false;
            foreach (var path in itemPaths)
            {
                foreach (var prefix in prefixes)
                {
                    if (path.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        matches.Add(key);
                        matched = true;
                        break;
                    }
                }

                if (matched)
                    break;
            }
        }

        return matches;
    }

    private static bool MatchesEstCustomization(EstIdentifier identifier, GlamourerCustomizationFilter filter)
    {
        if (identifier.GenderRace != Names.CombinedRace(filter.Gender, filter.Race))
            return false;

        return identifier.Slot switch
        {
            EstType.Hair => identifier.SetId.Id == filter.Hair.Value,
            EstType.Face => identifier.SetId.Id == filter.Face.Value,
            _ => false,
        };
    }
}
