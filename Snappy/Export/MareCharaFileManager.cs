using LZ4;
using Snappy;
using Snappy.Models;
using Snappy.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MareSynchronos.Export;
public class MareCharaFileManager
{
    private readonly Configuration _configuration;
    private readonly Plugin _plugin;
    public MareCharaFileHeader? LoadedCharaFile { get; private set; }

    public MareCharaFileManager(Plugin plugin)
    {
        _plugin = plugin;
        _configuration = plugin.Configuration;
    }

    public void ClearMareCharaFile()
    {
        LoadedCharaFile = null;
    }

    public void LoadMareCharaFile(string filePath)
    {
        try
        {
            using var unwrapped = File.OpenRead(filePath);
            using var lz4Stream = new LZ4Stream(unwrapped, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
            using var reader = new BinaryReader(lz4Stream);
            LoadedCharaFile = MareCharaFileHeader.FromBinaryReader(filePath, reader);
            Logger.Debug("Read Mare Chara File");
            Logger.Debug("Version: " + LoadedCharaFile.Version);

        }
        catch { throw; }
    }

    private Dictionary<string, string> ExtractFilesFromCharaFile(MareCharaFileHeader charaFileHeader, BinaryReader reader)
    {
        Dictionary<string, string> gamePathToFilePath = new(StringComparer.Ordinal);
        int i = 0;
        foreach (var fileData in charaFileHeader.CharaFileData.Files)
        {
            var fileName = Path.Combine(_configuration.WorkingDirectory, "mcdf_" + charaFileHeader.CharaFileData.Description, "mare_" + (i++) + ".tmp");
            var length = fileData.Length;
            var bufferSize = 4 * 1024 * 1024;
            var buffer = new byte[bufferSize];
            using var fs = File.OpenWrite(fileName);
            using var wr = new BinaryWriter(fs);
            while (length > 0)
            {
                if (length < bufferSize) bufferSize = (int)length;
                buffer = reader.ReadBytes(bufferSize);
                wr.Write(length > bufferSize ? buffer : buffer.Take((int)length).ToArray());
                length -= bufferSize;
            }
            foreach (var path in fileData.GamePaths)
            {
                gamePathToFilePath[path] = Path.GetFileName(fileName);
                Logger.Verbose(path + " => " + fileName);
            }
        }

        return gamePathToFilePath;
    }

    public void ExtractMareCharaFile()
    {
        Dictionary<string, string> extractedFiles = new();
        try
        {
            if (LoadedCharaFile == null || !File.Exists(LoadedCharaFile.FilePath)) return;
            var path = Path.Combine(_configuration.WorkingDirectory, "mcdf_" + LoadedCharaFile.CharaFileData.Description);
            //create directory if needed
            if (Directory.Exists(path))
            {
                Logger.Warn("Snapshot already existed, deleting");
                Directory.Delete(path, true);
            }
            Directory.CreateDirectory(path);

            //extract files
            using var unwrapped = File.OpenRead(LoadedCharaFile.FilePath);
            using var lz4Stream = new LZ4Stream(unwrapped, LZ4StreamMode.Decompress, LZ4StreamFlags.HighCompression);
            using var reader = new BinaryReader(lz4Stream);
            LoadedCharaFile.AdvanceReaderToData(reader);
            extractedFiles = ExtractFilesFromCharaFile(LoadedCharaFile, reader);

            //generate snapper JSON manifest
            SnapshotInfo snapInfo = new SnapshotInfo();
            snapInfo.GlamourerString = LoadedCharaFile.CharaFileData.GlamourerData;
            snapInfo.ManipulationString = LoadedCharaFile.CharaFileData.ManipulationData;
            snapInfo.CustomizeData = LoadedCharaFile.CharaFileData.CustomizePlusData;
            snapInfo.FileReplacements = new();
            foreach (var record in extractedFiles)
            {
                if (snapInfo.FileReplacements.ContainsKey(record.Value))
                {
                    snapInfo.FileReplacements[record.Value].Add(record.Key);
                }
                else
                {
                    snapInfo.FileReplacements.Add(record.Value, new List<string>() { record.Key });
                }
            }

            if (!string.IsNullOrEmpty(snapInfo.CustomizeData))
            {
                try
                {
                    // The data in the MCDF is the Base64 of the raw JSON profile. Decode it.
                    var cPlusJson = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(snapInfo.CustomizeData));

                    // The 'path' variable is already defined earlier in the method.
                    var templateString = _plugin.SnapshotManager.CreateCustomizePlusTemplate(cPlusJson, LoadedCharaFile.CharaFileData.Description);

                    if (!string.IsNullOrEmpty(templateString))
                    {
                        File.WriteAllText(Path.Combine(path, "customizePlus.json"), templateString);
                        Logger.Debug("Successfully created customizePlus.json from imported MCDF data.");
                    }
                }
                catch (Exception e)
                {
                    Logger.Error("Failed to create customizePlus.json from imported MCDF data.", e);
                }
            }

            string infoJson = JsonSerializer.Serialize(snapInfo);
            File.WriteAllText(Path.Combine(_configuration.WorkingDirectory, "mcdf_" + LoadedCharaFile.CharaFileData.Description, "snapshot.json"), infoJson);
        }
        catch { throw; }
    }
}