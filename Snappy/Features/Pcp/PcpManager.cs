using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Penumbra.GameData.Files.AtchStructs;
using Penumbra.GameData.Structs;
using Penumbra.Meta.Manipulations;
using Snappy.Common;
using Snappy.Common.Utilities;
using Snappy.Features.Packaging;
using Snappy.Models;
using Snappy.Services.SnapshotManager;

namespace Snappy.Features.Pcp;

public class PcpManager : IPcpManager
{
    private readonly Configuration _configuration;
    private readonly ISnapshotFileService _snapshotFileService;
    private readonly Action _snapshotsUpdatedCallback;

    public PcpManager(Configuration configuration, ISnapshotFileService snapshotFileService,
        Action snapshotsUpdatedCallback)
    {
        _configuration = configuration;
        _snapshotFileService = snapshotFileService;
        _snapshotsUpdatedCallback = snapshotsUpdatedCallback;
    }

    public void ImportPcp(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Notify.Error($"PCP file not found: {filePath}");
                return;
            }

            using var archive = ZipFile.OpenRead(filePath);

            // Read metadata
            var metadata = ArchiveUtil.ReadJsonEntry<PcpMetadata>(archive, "meta.json", Notify.Error,
                "Invalid PCP file: missing meta.json", "Failed to parse meta.json from PCP file.");
            if (metadata == null) return;

            // Read character data
            var characterData = ArchiveUtil.ReadJsonEntry<PcpCharacterData>(archive, "character.json", Notify.Error,
                "Invalid PCP file: missing character.json", "Failed to parse character.json from PCP file.");
            if (characterData == null) return;

            // Read mod data
            var modData = ArchiveUtil.ReadJsonEntry<PcpModData>(archive, "default_mod.json", Notify.Error,
                "Invalid PCP file: missing default_mod.json", "Failed to parse default_mod.json from PCP file.");
            if (modData == null) return;

            // Create snapshot
            var snapshotPath = CreateSnapshotDirectory(metadata.Name);
            var paths = SnapshotPaths.From(snapshotPath);
            Directory.CreateDirectory(paths.FilesDirectory);

            // Extract files
            var gamePathToHashMap = ExtractFiles(archive, paths.FilesDirectory, modData);

            // Create snapshot info
            var snapshotInfo = CreateSnapshotInfo(metadata, characterData, snapshotPath, gamePathToHashMap, modData);

            // Create Customize+ history
            var customizeHistory = new CustomizeHistory();
            if (characterData.CustomizePlus != null)
                customizeHistory = CreateCustomizeHistory(characterData, snapshotInfo.CurrentFileMapId);
            var customizeData = customizeHistory.Entries.LastOrDefault()?.CustomizeData ?? string.Empty;

            // Create Glamourer history
            var glamourerHistory = new GlamourerHistory();
            if (characterData.Glamourer != null)
                glamourerHistory = CreateGlamourerHistory(characterData, snapshotInfo.CurrentFileMapId, customizeData);

            // Save all data to disk
            _snapshotFileService.SaveSnapshotToDisk(paths.RootPath, snapshotInfo, glamourerHistory, customizeHistory);

            _snapshotsUpdatedCallback();
            Notify.Success($"Successfully imported PCP: {metadata.Name}");
        }
        catch (Exception ex)
        {
            Notify.Error($"Failed during PCP import for file: {Path.GetFileName(filePath)}\n{ex.Message}");
            PluginLog.Error($"Failed during PCP import for file: {Path.GetFileName(filePath)}: {ex}");
            throw;
        }
    }

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
            modData.Manipulations = ModPackageBuilder.BuildManipulations(snapshotInfo.ManipulationString);
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

    private string CreateSnapshotDirectory(string description)
    {
        var snapshotDirName = string.IsNullOrEmpty(description)
            ? $"PCP_Import_{DateTime.Now:yyyyMMddHHmmss}"
            : SanitizeDirectoryName(description);

        var snapshotPath = Path.Combine(_configuration.WorkingDirectory, snapshotDirName);

        if (Directory.Exists(snapshotPath)) Directory.Delete(snapshotPath, true);

        Directory.CreateDirectory(snapshotPath);
        return snapshotPath;
    }

    private static string SanitizeDirectoryName(string name)
    {
        // Remove or replace invalid characters for Windows directory names
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = name;

        foreach (var invalidChar in invalidChars) sanitized = sanitized.Replace(invalidChar, '_');

        // Also replace colon specifically (which might not be in GetInvalidFileNameChars on all systems)
        sanitized = sanitized.Replace(':', '_');

        // Trim whitespace and dots from the end (Windows doesn't like these)
        sanitized = sanitized.TrimEnd(' ', '.');

        // Ensure the name isn't empty after sanitization
        if (string.IsNullOrWhiteSpace(sanitized)) sanitized = $"PCP_Import_{DateTime.Now:yyyyMMddHHmmss}";

        return sanitized;
    }

    private static SnapshotInfo CreateSnapshotInfo(PcpMetadata metadata, PcpCharacterData characterData,
        string snapshotPath, Dictionary<string, string> gamePathToHashMap, PcpModData modData)
    {
        // Convert PCP manipulations to Penumbra Base64 format
        var manipulationString = string.Empty;
        if (modData.Manipulations.Count > 0)
            try
            {
                manipulationString = ConvertPcpManipulationsToPenumbraFormat(modData.Manipulations);
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"Failed to process manipulations from PCP: {ex.Message}");
            }

        var snapshotInfo = new SnapshotInfo
        {
            SourceActor = characterData.Actor.PlayerName,
            SourceWorldId = characterData.Actor.HomeWorld,
            LastUpdate = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            FileReplacements = gamePathToHashMap,
            ManipulationString = manipulationString
        };

        if (snapshotInfo.FileReplacements.Any())
        {
            var baseId = Guid.NewGuid().ToString("N");
            snapshotInfo.FileMaps.Add(new FileMapEntry
            {
                Id = baseId,
                BaseId = null,
                Changes = new Dictionary<string, string>(snapshotInfo.FileReplacements,
                    StringComparer.OrdinalIgnoreCase),
                Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            });
            snapshotInfo.CurrentFileMapId = baseId;
        }

        return snapshotInfo;
    }

    private static string ConvertPcpManipulationsToPenumbraFormat(List<JObject> pcpManipulations)
    {
        // Convert PCP manipulations to Penumbra's expected format
        // Penumbra expects: Base64(GZip(VersionByte + Data))

        // For now, we'll use version 0 format (JSON)
        var manipulationsJson = JsonConvert.SerializeObject(pcpManipulations);
        var jsonBytes = Encoding.UTF8.GetBytes(manipulationsJson);

        using var resultStream = new MemoryStream();
        using (var gzipStream = new GZipStream(resultStream, CompressionMode.Compress))
        {
            // Write version byte (0 for JSON format)
            gzipStream.WriteByte(0);
            // Write the JSON data
            gzipStream.Write(jsonBytes, 0, jsonBytes.Length);
        }

        return Convert.ToBase64String(resultStream.ToArray());
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

    private static GlamourerHistory CreateGlamourerHistory(PcpCharacterData characterData, string? fileMapId,
        string? customizeData)
    {
        var history = new GlamourerHistory();
        if (characterData.Glamourer != null)
            try
            {
                // Parse the Glamourer object from PCP
                var glamourerObj = JObject.FromObject(characterData.Glamourer);

                // Check if this is the new Glamourer PCP format with Version and Design
                if (glamourerObj["Version"]?.ToObject<int>() == 1 && glamourerObj["Design"] != null)
                {
                    // This is the new Glamourer PCP format - convert the Design to Base64
                    var designJson = JsonConvert.SerializeObject(glamourerObj["Design"], Formatting.None);
                    var designBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(designJson));
                    history.Entries.Add(GlamourerHistoryEntry.Create(designBase64, "Imported from PCP", fileMapId,
                        customizeData));
                }
                else
                {
                    // This might be an older format or different structure, attempt to import as legacy
                    PluginLog.Debug("PCP Glamourer data is not in V1 format, attempting to import as legacy data.");
                    var designJson = JsonConvert.SerializeObject(glamourerObj, Formatting.None);
                    var designBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(designJson));
                    history.Entries.Add(GlamourerHistoryEntry.Create(designBase64,
                        "Imported from PCP (Legacy Format)", fileMapId, customizeData));
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"Failed to process Glamourer data from PCP: {ex.Message}");
            }

        return history;
    }

    private static CustomizeHistory CreateCustomizeHistory(PcpCharacterData characterData, string? fileMapId)
    {
        var history = new CustomizeHistory();
        if (characterData.CustomizePlus != null)
            try
            {
                // Parse the CustomizePlus object from PCP
                var customizePlusObj = JObject.FromObject(characterData.CustomizePlus);

                // Check if this is the new Customize+ PCP format with Template
                if (customizePlusObj["Template"] != null)
                {
                    // This is the new Customize+ PCP format - extract the Template
                    var templateJson = JsonConvert.SerializeObject(customizePlusObj["Template"], Formatting.None);

                    // Convert to Base64 for Snappy's format
                    var customizeBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(templateJson));
                    var customizeEntry =
                        CustomizeHistoryEntry.CreateFromBase64(customizeBase64, templateJson, "Imported from PCP",
                            fileMapId);
                    history.Entries.Add(customizeEntry);
                }
                else
                {
                    // This might be an older format or different structure
                    var customizeJson = JsonConvert.SerializeObject(characterData.CustomizePlus);
                    var customizeBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(customizeJson));
                    var customizeEntry = CustomizeHistoryEntry.CreateFromBase64(customizeBase64, customizeJson,
                        "Imported from PCP (Legacy Format)", fileMapId);
                    history.Entries.Add(customizeEntry);
                }
            }
            catch (Exception ex)
            {
                PluginLog.Warning($"Failed to process Customize+ data from PCP: {ex.Message}");
            }

        return history;
    }

    private static Dictionary<string, string> ExtractFiles(ZipArchive archive, string filesDir, PcpModData modData)
    {
        var gamePathToHashMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (gamePath, archivePath) in modData.Files)
        {
            var entry = archive.GetEntry(archivePath.Replace('\\', '/'));
            if (entry == null)
            {
                PluginLog.Warning($"PCP file missing: {archivePath}");
                continue;
            }

            var fileName = Path.GetFileName(archivePath);
            var hash = Path.GetFileNameWithoutExtension(fileName);
            var existingPath = SnapshotBlobUtil.FindAnyExistingBlobPath(filesDir, hash);
            var outputPath = existingPath ?? SnapshotBlobUtil.GetPreferredBlobPath(filesDir, hash, gamePath);

            if (!File.Exists(outputPath))
            {
                using var entryStream = entry.Open();
                using var outputStream = File.Create(outputPath);
                entryStream.CopyTo(outputStream);
            }

            // Store the hash for the game path
            gamePathToHashMap[gamePath] = hash;
        }

        return gamePathToHashMap;
    }
}
