using System.IO.Compression;
using Newtonsoft.Json.Linq;
using Snappy.Common;
using Snappy.Features.Packaging;

namespace Snappy.Features.Pcp;

internal sealed class PcpExportService
{
    public async Task ExportPcp(
        string snapshotPath,
        string outputPath,
        GlamourerHistoryEntry? selectedGlamourer,
        CustomizeHistoryEntry? selectedCustomize,
        string? playerNameOverride,
        int? homeWorldIdOverride)
    {
        string? temporaryOutput = null;
        try
        {
            var paths = SnapshotPaths.From(snapshotPath);

            var snapshotInfo = await JsonUtil.DeserializeStateAsync<SnapshotInfo>(paths.SnapshotFile);
            if (snapshotInfo == null)
            {
                Notify.Error("Failed to load snapshot info for PCP export");
                return;
            }

            var glamourerHistory = await JsonUtil.DeserializeStateAsync<GlamourerHistory>(paths.GlamourerHistoryFile) ??
                                   new GlamourerHistory();
            var customizeHistory = await JsonUtil.DeserializeStateAsync<CustomizeHistory>(paths.CustomizeHistoryFile) ??
                                   new CustomizeHistory();

            var fileMapId = selectedGlamourer?.FileMapId ?? selectedCustomize?.FileMapId ?? snapshotInfo.CurrentFileMapId;
            var resolvedFileMap = FileMapUtil.ResolveFileMap(snapshotInfo, fileMapId);
            var resolvedFileSwaps = FileMapUtil.ResolveFileSwaps(snapshotInfo, fileMapId);
            foreach (var gamePath in resolvedFileSwaps.Keys)
                resolvedFileMap.Remove(gamePath);
            var resolvedManipulations = FileMapUtil.ResolveManipulation(snapshotInfo, fileMapId);

            var pcpOutputPath = Path.ChangeExtension(outputPath, ".pcp");
            temporaryOutput = AtomicFileUtil.CreateTemporaryOutputPath(pcpOutputPath);
            using (var archive = ZipFile.Open(temporaryOutput, ZipArchiveMode.Create))
            {
                var actorName = !string.IsNullOrWhiteSpace(playerNameOverride)
                    ? playerNameOverride!
                    : snapshotInfo.SourceActor;
                if (string.IsNullOrWhiteSpace(actorName))
                    actorName = Path.GetFileName(snapshotPath);
                var actorHomeWorld = ResolveActorHomeWorld(homeWorldIdOverride ?? snapshotInfo.SourceWorldId);

                var snapshotName = Path.GetFileName(snapshotPath);
                var meta = ModMetadataBuilder.BuildSnapshotMetadata(snapshotName);
                ArchiveUtil.WriteJsonEntry(archive, "meta.json", meta);

                var characterData = new PcpCharacterData
                {
                    Version = 1,
                    Actor = new PcpActor
                    {
                        Type = "Player",
                        PlayerName = actorName,
                        HomeWorld = actorHomeWorld
                    },
                    Mod = actorName,
                    Collection = actorName,
                    Time = DateTime.TryParse(snapshotInfo.LastUpdate, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var lastUpdateUtc)
                        ? lastUpdateUtc
                        : DateTime.UtcNow,
                    Note = "Exported from Snappy"
                };

                var glamourerEntry = selectedGlamourer ?? glamourerHistory.Entries.LastOrDefault();
                if (glamourerEntry != null)
                    try
                    {
                        if (GlamourerDesignUtil.TryDecodeDesignJson(glamourerEntry.GlamourerString, out var designObj)
                            && designObj != null)
                        {
                            characterData.Glamourer = new JObject
                            {
                                ["Version"] = 1,
                                ["Design"] = designObj
                            };
                        }
                        else
                        {
                            throw new InvalidDataException("Glamourer data was empty after decoding/decompression.");
                        }
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Warning(
                            $"Failed to export Glamourer data to PCP for snapshot '{snapshotName}': {ex.Message}");
                    }

                var customizeEntry = selectedCustomize ?? customizeHistory.Entries.LastOrDefault();
                if (TryCreateCustomizePlusPcpData(customizeEntry, actorName, out var customizePlus))
                    characterData.CustomizePlus = customizePlus;

                ArchiveUtil.WriteJsonEntry(archive, "character.json", characterData);

                var modData = new PcpModData
                {
                    Manipulations = ModPackageBuilder.BuildManipulations(resolvedManipulations),
                    FileSwaps = new Dictionary<string, string>(resolvedFileSwaps, StringComparer.OrdinalIgnoreCase)
                };
                ModPackageBuilder.AddSnapshotFiles(archive, snapshotInfo, paths.FilesDirectory, modData.Files,
                    resolvedFileMap);
                ArchiveUtil.WriteJsonEntry(archive, "default_mod.json", modData);
            }

            AtomicFileUtil.Complete(temporaryOutput, pcpOutputPath);
            temporaryOutput = null;
            Notify.Success($"Successfully exported PCP: {pcpOutputPath}");
        }
        catch (Exception ex)
        {
            Notify.Error($"Failed during PCP export: {ex.Message}");
            PluginLog.Error($"Failed during PCP export for '{snapshotPath}': {ex}");
        }
        finally
        {
            if (temporaryOutput != null)
                AtomicFileUtil.TryDelete(temporaryOutput);
        }
    }

    private static int ResolveActorHomeWorld(int? worldId)
        => worldId is > 0 and <= ushort.MaxValue ? worldId.Value : PcpActor.AnyWorld;

    private static bool TryCreateCustomizePlusPcpData(CustomizeHistoryEntry? entry, string actorName,
        out JObject? customizePlus)
    {
        customizePlus = null;
        if (entry == null)
            return false;

        var profileJson = DecodeCustomizeData(entry.CustomizeData);
        if (string.IsNullOrWhiteSpace(profileJson))
            profileJson = CustomizePlusUtil.DecompressTemplateBase64(entry.CustomizeTemplate);

        if (!CustomizePlusUtil.TryCreateTemplateJson(profileJson, out var templateJson, $"PCP Template - {actorName}"))
        {
            PluginLog.Warning("Failed to convert Customize+ data to a PCP template.");
            return false;
        }

        customizePlus = new JObject
        {
            ["Template"] = JObject.Parse(templateJson)
        };
        return true;
    }

    private static string DecodeCustomizeData(string customizeData)
    {
        if (string.IsNullOrWhiteSpace(customizeData))
            return string.Empty;

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(customizeData));
        }
        catch (FormatException)
        {
            return customizeData;
        }
    }

}
