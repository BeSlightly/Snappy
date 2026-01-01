using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Snappy.Common;
using Snappy.Features.Packaging;

namespace Snappy.Features.Pcp;

internal sealed class PcpExportService
{
    public async Task ExportPcp(string snapshotPath, string outputPath)
    {
        await ExportPcp(snapshotPath, outputPath, null, null);
    }

    public async Task ExportPcp(string snapshotPath, string outputPath, GlamourerHistoryEntry? selectedGlamourer,
        CustomizeHistoryEntry? selectedCustomize)
    {
        await ExportPcp(snapshotPath, outputPath, selectedGlamourer, selectedCustomize, null, null);
    }

    public async Task ExportPcp(
        string snapshotPath,
        string outputPath,
        GlamourerHistoryEntry? selectedGlamourer,
        CustomizeHistoryEntry? selectedCustomize,
        string? playerNameOverride,
        int? homeWorldIdOverride)
    {
        try
        {
            var paths = SnapshotPaths.From(snapshotPath);

            var snapshotInfo = await JsonUtil.DeserializeAsync<SnapshotInfo>(paths.SnapshotFile);
            if (snapshotInfo == null)
            {
                Notify.Error("Failed to load snapshot info for PCP export");
                return;
            }

            var glamourerHistory = await JsonUtil.DeserializeAsync<GlamourerHistory>(paths.GlamourerHistoryFile) ??
                                   new GlamourerHistory();
            var customizeHistory = await JsonUtil.DeserializeAsync<CustomizeHistory>(paths.CustomizeHistoryFile) ??
                                   new CustomizeHistory();

            var fileMapId = selectedGlamourer?.FileMapId ?? selectedCustomize?.FileMapId ?? snapshotInfo.CurrentFileMapId;
            var resolvedFileMap = FileMapUtil.ResolveFileMap(snapshotInfo, fileMapId);
            if (!resolvedFileMap.Any())
                resolvedFileMap = new Dictionary<string, string>(snapshotInfo.FileReplacements,
                    StringComparer.OrdinalIgnoreCase);
            var resolvedManipulations = FileMapUtil.ResolveManipulation(snapshotInfo, fileMapId);

            using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);

            // Resolve actor name and homeworld to use across PCP
            var actorName = !string.IsNullOrWhiteSpace(playerNameOverride)
                ? playerNameOverride!
                : snapshotInfo.SourceActor;
            var actorHomeWorld = homeWorldIdOverride ?? snapshotInfo.SourceWorldId ?? 40;

            // Create metadata
            var snapshotName = Path.GetFileName(snapshotPath);
            var meta = ModMetadataBuilder.BuildSnapshotMetadata(snapshotName, snapshotInfo.SourceActor);

            ArchiveUtil.WriteJsonEntry(archive, "meta.json", meta);

            // Create character data with proper structure for Penumbra
            var characterData = new PcpCharacterData
            {
                Version = 1,
                Actor = new PcpActor
                {
                    Type = "Player",
                    PlayerName = actorName,
                    HomeWorld = actorHomeWorld
                },
                Mod = actorName, // Use specified/actor name as mod name
                Collection = actorName, // Use specified/actor name for collection
                Time = DateTime.TryParse(snapshotInfo.LastUpdate, out var lastUpdate) ? lastUpdate : DateTime.Now,
                Note = "Exported from Snappy"
            };

            // Determine which Glamourer entry to export (prefer selected, otherwise latest if available)
            var glamourerEntry = selectedGlamourer ??
                                 (glamourerHistory?.Entries.Count > 0 ? glamourerHistory.Entries.Last() : null);
            if (glamourerEntry != null)
                try
                {
                    var dataBytes = Convert.FromBase64String(glamourerEntry.GlamourerString);
                    string designJson;

                    // Find Gzip header, accounting for a potential version byte prefix.
                    var gzipStartIndex = -1;
                    if (dataBytes.Length > 2 && dataBytes[0] == 0x1F && dataBytes[1] == 0x8B)
                        gzipStartIndex = 0;
                    else if (dataBytes.Length > 3 && dataBytes[1] == 0x1F && dataBytes[2] == 0x8B)
                        gzipStartIndex = 1;

                    if (gzipStartIndex != -1)
                    {
                        using var compressedStream =
                            new MemoryStream(dataBytes, gzipStartIndex, dataBytes.Length - gzipStartIndex);
                        using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
                        using var reader = new StreamReader(gzipStream, Encoding.UTF8);
                        designJson = reader.ReadToEnd();
                    }
                    else
                    {
                        designJson = Encoding.UTF8.GetString(dataBytes);
                    }

                    if (!string.IsNullOrWhiteSpace(designJson))
                    {
                        var designObj = JObject.Parse(designJson);
                        characterData.Glamourer = new JObject
                        {
                            ["Version"] = 1,
                            ["Design"] = designObj
                        };
                    }
                    else
                    {
                        throw new Exception("Glamourer data was empty after decoding/decompression.");
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(
                        $"Failed to export Glamourer data to PCP for snapshot '{snapshotName}': {ex.Message}");
                }

            // Determine which Customize+ entry to export (prefer selected, otherwise latest if available)
            var customizeEntry = selectedCustomize ??
                                 (customizeHistory?.Entries.Count > 0 ? customizeHistory.Entries.Last() : null);
            if (customizeEntry != null && !string.IsNullOrEmpty(customizeEntry.CustomizeTemplate))
                try
                {
                    var templateJson = DecompressCustomizeTemplate(customizeEntry.CustomizeTemplate);
                    if (!string.IsNullOrEmpty(templateJson))
                    {
                        var templateObj = JObject.Parse(templateJson);

                        if (templateObj["CreationDate"] == null)
                            templateObj["CreationDate"] = DateTimeOffset.UtcNow;
                        if (templateObj["ModifiedDate"] == null)
                            templateObj["ModifiedDate"] = DateTimeOffset.UtcNow;
                        if (templateObj["UniqueId"] == null)
                            templateObj["UniqueId"] = Guid.NewGuid();
                        if (templateObj["Name"] == null)
                            templateObj["Name"] = $"PCP Template - {actorName}";
                        if (templateObj["Author"] == null)
                            templateObj["Author"] = "Snappy Export";
                        if (templateObj["Description"] == null)
                            templateObj["Description"] = "Template exported from Snappy";
                        if (templateObj["Version"] == null)
                            templateObj["Version"] = 4;
                        if (templateObj["IsWriteProtected"] == null)
                            templateObj["IsWriteProtected"] = false;

                        if (templateObj["Bones"] is JObject bones)
                            foreach (var bone in bones.Properties())
                                if (bone.Value is JObject boneObj)
                                {
                                    if (boneObj["PropagateTranslation"] == null)
                                        boneObj["PropagateTranslation"] = false;
                                    if (boneObj["PropagateRotation"] == null)
                                        boneObj["PropagateRotation"] = false;
                                    if (boneObj["PropagateScale"] == null)
                                        boneObj["PropagateScale"] = false;
                                }

                        characterData.CustomizePlus = new JObject
                        {
                            ["Template"] = templateObj
                        };
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(
                        $"Failed to export Customize+ data to PCP for snapshot '{snapshotName}': {ex.Message}");
                }

            ArchiveUtil.WriteJsonEntry(archive, "character.json", characterData);

            // Create mod data and add files
            var modData = new PcpModData();
            modData.Manipulations = ModPackageBuilder.BuildManipulations(resolvedManipulations);
            ModPackageBuilder.AddSnapshotFiles(archive, snapshotInfo, paths.FilesDirectory, modData.Files,
                resolvedFileMap);

            // Add default_mod.json
            ArchiveUtil.WriteJsonEntry(archive, "default_mod.json", modData);

            Notify.Success($"Successfully exported PCP: {outputPath}");
        }
        catch (Exception ex)
        {
            Notify.Error($"Failed during PCP export: {ex.Message}");
        }
    }

    private static string DecompressCustomizeTemplate(string base64Template)
    {
        try
        {
            var compressedBytes = Convert.FromBase64String(base64Template);

            using var compressedStream = new MemoryStream(compressedBytes);
            using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var resultStream = new MemoryStream();

            gzipStream.CopyTo(resultStream);
            var decompressedBytes = resultStream.ToArray();

            // Skip the version byte (first byte)
            if (decompressedBytes.Length > 1)
            {
                var versionByte = decompressedBytes[0];
                PluginLog.Debug($"Decompressing C+ template version: {versionByte}");
                var jsonBytes = decompressedBytes[1..];
                return Encoding.UTF8.GetString(jsonBytes);
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"Failed to decompress Customize+ template: {ex.Message}");
            return string.Empty;
        }
    }
}
