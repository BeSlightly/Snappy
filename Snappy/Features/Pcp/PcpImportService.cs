using System.IO.Compression;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Snappy.Common;
using Snappy.Services.SnapshotManager;

namespace Snappy.Features.Pcp;

internal sealed class PcpImportService
{
    private readonly Configuration _configuration;
    private readonly ISnapshotFileService _snapshotFileService;
    private readonly Action _snapshotsUpdatedCallback;

    public PcpImportService(Configuration configuration, ISnapshotFileService snapshotFileService,
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
            var snapshotInfo = CreateSnapshotInfo(characterData, gamePathToHashMap, modData);

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
        }
    }

    private string CreateSnapshotDirectory(string description)
    {
        var snapshotDirName = SnapshotImportUtil.SanitizeDirectoryName(description, "PCP_Import");
        return SnapshotImportUtil.CreateUniqueSnapshotDirectory(_configuration.WorkingDirectory, snapshotDirName);
    }

    private static SnapshotInfo CreateSnapshotInfo(PcpCharacterData characterData,
        Dictionary<string, string> gamePathToHashMap, PcpModData modData)
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

        return SnapshotImportUtil.BuildSnapshotInfo(characterData.Actor.PlayerName, characterData.Actor.HomeWorld,
            manipulationString, gamePathToHashMap);
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
                if (glamourerObj["Version"]?.ToObject<int>() == 1 && glamourerObj["Design"] is JObject designObj)
                {
                    // This is the new Glamourer PCP format - convert the Design to Base64
                    if (GlamourerDesignUtil.TryEncodeDesignJson(designObj, out var designBase64))
                        history.Entries.Add(GlamourerHistoryEntry.Create(designBase64, "Imported from PCP", fileMapId,
                            customizeData));
                    else
                        PluginLog.Warning("Failed to encode Glamourer Design data from PCP.");
                }
                else
                {
                    // This might be an older format or different structure, attempt to import as legacy data.
                    PluginLog.Debug("PCP Glamourer data is not in V1 format, attempting to import as legacy data.");
                    if (GlamourerDesignUtil.TryEncodeDesignJson(glamourerObj, out var designBase64))
                        history.Entries.Add(GlamourerHistoryEntry.Create(designBase64,
                            "Imported from PCP (Legacy Format)", fileMapId, customizeData));
                    else
                        PluginLog.Warning("Failed to encode Glamourer legacy data from PCP.");
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
