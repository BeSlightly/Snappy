using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using LZ4;
using Snappy.Core;
using Snappy.Models;
using Snappy.Utils;

namespace Snappy.Features.Mcdf
{
    public class McdfManager
    {
        private readonly Plugin _plugin;
        private readonly Configuration _configuration;
        private readonly SnapshotManager _snapshotManager;

        public McdfManager(Plugin plugin, SnapshotManager snapshotManager)
        {
            _plugin = plugin;
            _configuration = plugin.Configuration;
            _snapshotManager = snapshotManager;
        }

        private Dictionary<string, string> ExtractAndHashMapFiles(
            McdfHeader charaFileHeader,
            BinaryReader reader,
            string filesDir
        )
        {
            Dictionary<string, string> gamePathToHash = new(StringComparer.OrdinalIgnoreCase);

            foreach (var fileData in charaFileHeader.CharaFileData.Files)
            {
                var length = fileData.Length;
                if (length == 0)
                    continue;

                var buffer = reader.ReadBytes((int)length);
                if (buffer.Length != length)
                {
                    PluginLog.Error(
                        $"MCDF Read Error: Expected {length} bytes, got {buffer.Length}. File may be corrupt."
                    );
                    continue;
                }

                var hash = Crypto.GetHash(buffer);
                var hashedFilePath = Path.Combine(filesDir, hash + ".dat");

                if (!File.Exists(hashedFilePath))
                {
                    File.WriteAllBytes(hashedFilePath, buffer);
                }

                foreach (var path in fileData.GamePaths)
                {
                    gamePathToHash[path] = hash;
                }
            }

            return gamePathToHash;
        }

        public void ImportMcdf(string filePath)
        {
            try
            {
                using var fileStream = File.OpenRead(filePath);
                using var lz4Stream = new LZ4Stream(
                    fileStream,
                    LZ4StreamMode.Decompress,
                    LZ4StreamFlags.HighCompression
                );
                using var memoryStream = new MemoryStream();
                lz4Stream.CopyTo(memoryStream);
                memoryStream.Position = 0;

                using var reader = new BinaryReader(memoryStream);

                var loadedCharaFile = McdfHeader.FromBinaryReader(filePath, reader);
                if (loadedCharaFile == null)
                {
                    Notify.Error($"Failed to read MCDF header from {Path.GetFileName(filePath)}.");
                    return;
                }

                PluginLog.Debug("Read Mare Chara File. Version: " + loadedCharaFile.Version);

                var snapshotDirName = string.IsNullOrEmpty(
                    loadedCharaFile.CharaFileData.Description
                )
                    ? $"MCDF_Import_{DateTime.Now:yyyyMMddHHmmss}"
                    : loadedCharaFile.CharaFileData.Description;

                var snapshotPath = Path.Combine(_configuration.WorkingDirectory, snapshotDirName);
                var filesPath = Path.Combine(snapshotPath, "_files");

                if (Directory.Exists(snapshotPath))
                {
                    PluginLog.Debug("Snapshot from MCDF already existed, deleting");
                    Directory.Delete(snapshotPath, true);
                }
                Directory.CreateDirectory(snapshotPath);
                Directory.CreateDirectory(filesPath);

                Dictionary<string, string> gamePathToHashMap = ExtractAndHashMapFiles(
                    loadedCharaFile,
                    reader,
                    filesPath
                );

                SnapshotInfo snapInfo = new SnapshotInfo
                {
                    SourceActor = snapshotDirName,
                    LastUpdate = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    ManipulationString = loadedCharaFile.CharaFileData.ManipulationData,
                    FileReplacements = gamePathToHashMap,
                };

                GlamourerHistory glamourerHistory = new GlamourerHistory();
                glamourerHistory.Entries.Add(
                    GlamourerHistoryEntry.Create(
                        loadedCharaFile.CharaFileData.GlamourerData,
                        "Imported from MCDF"
                    )
                );

                CustomizeHistory customizeHistory = new CustomizeHistory();
                var cplusData = loadedCharaFile.CharaFileData.CustomizePlusData;
                if (!string.IsNullOrEmpty(cplusData))
                {
                    var cplusJson = Encoding.UTF8.GetString(Convert.FromBase64String(cplusData));
                    var customizeEntry = CustomizeHistoryEntry.CreateFromBase64(
                        cplusData,
                        cplusJson,
                        "Imported from MCDF"
                    );
                    customizeHistory.Entries.Add(customizeEntry);
                }

                _snapshotManager.SaveSnapshotToDisk(
                    snapshotPath,
                    snapInfo,
                    glamourerHistory,
                    customizeHistory
                );

                Notify.Success(
                    $"Successfully imported '{Path.GetFileName(filePath)}' as new snapshot '{snapshotDirName}'."
                );
                _plugin.InvokeSnapshotsUpdated();
            }
            catch (Exception ex)
            {
                Notify.Error(
                    $"Failed during MCDF extraction for file: {Path.GetFileName(filePath)}\n{ex.Message}"
                );
                PluginLog.Error(
                    $"Failed during MCDF extraction for file: {Path.GetFileName(filePath)}: {ex}"
                );
                throw;
            }
        }
    }
}
