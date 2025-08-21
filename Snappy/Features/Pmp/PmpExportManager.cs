using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using Penumbra.GameData.Files.AtchStructs;
using Penumbra.GameData.Structs;
using Penumbra.Meta.Manipulations;
using Snappy.Common;
using Snappy.Features.Pmp.Models;

namespace Snappy.Features.Pmp;

public class PmpExportManager : IPmpExportManager
{
    private readonly Configuration _configuration;

    public PmpExportManager(Configuration configuration)
    {
        _configuration = configuration;
    }

    public bool IsExporting { get; private set; }

    public async Task SnapshotToPMPAsync(string snapshotPath)
    {
        if (IsExporting)
        {
            Notify.Warning("An export is already in progress.");
            return;
        }

        IsExporting = true;
        try
        {
            PluginLog.Debug($"Operating on {snapshotPath}");

            var paths = SnapshotPaths.From(snapshotPath);
            var snapshotInfo = await JsonUtil.DeserializeAsync<SnapshotInfo>(paths.SnapshotFile);
            if (snapshotInfo == null)
            {
                Notify.Error("Export failed: Could not read snapshot.json.");
                return;
            }

            var snapshotName = new DirectoryInfo(snapshotPath).Name;
            var pmpFileName = $"{snapshotName}_{Guid.NewGuid():N}";
            var workingDirectory = PrepareWorkingDirectory(pmpFileName);

            CreatePmpMetadataFile(workingDirectory, snapshotName, snapshotInfo.SourceActor);
            var defaultMod = await CreateDefaultModAsync(snapshotInfo, paths.FilesDirectory, workingDirectory);
            CreateDefaultModFile(workingDirectory, defaultMod);

            var pmpOutputPath = Path.Combine(_configuration.WorkingDirectory, $"{pmpFileName}.pmp");

            await Task.Run(() => ZipFile.CreateFromDirectory(workingDirectory, pmpOutputPath));

            Directory.Delete(workingDirectory, true);
            Notify.Success($"Successfully exported {snapshotName} to {pmpOutputPath}");
        } catch (Exception e)
        {
            Notify.Error($"PMP export failed: {e.Message}");
            PluginLog.Error($"PMP export failed: {e}");
        } finally
        {
            IsExporting = false;
        }
    }

    private string PrepareWorkingDirectory(string pmpFileName)
    {
        var workingDirectory = Path.Combine(_configuration.WorkingDirectory, $"temp_{pmpFileName}");
        if (Directory.Exists(workingDirectory)) Directory.Delete(workingDirectory, true);
        Directory.CreateDirectory(workingDirectory);
        return workingDirectory;
    }

    private static void CreatePmpMetadataFile(string workingDir, string name, string sourceActor)
    {
        var metadata = new PmpMetadata
        {
            Name = name,
            Author = "Snapper",
            Description = $"A snapshot of {sourceActor}."
        };
        JsonUtil.Serialize(metadata, Path.Combine(workingDir, "meta.json"));
    }

    private static void CreateDefaultModFile(string workingDir, PmpDefaultMod mod)
    {
        JsonUtil.Serialize(mod, Path.Combine(workingDir, "default_mod.json"));
    }

    private static async Task<PmpDefaultMod> CreateDefaultModAsync(SnapshotInfo snapshotInfo, string sourceFilesDir,
        string pmpWorkingDir)
    {
        var defaultMod = new PmpDefaultMod();

        foreach (var (gamePath, hash) in snapshotInfo.FileReplacements)
        {
            var normalizedGamePath = gamePath.Replace('\\', '/').TrimStart('/');
            var sourceFilePath = Path.Combine(sourceFilesDir, $"{hash}{Constants.DataFileExtension}");

            if (!File.Exists(sourceFilePath))
            {
                PluginLog.Warning(
                    $"Missing file blob for {normalizedGamePath} (hash: {hash}). It will not be included in the PMP.");
                continue;
            }

            try
            {
                var destFilePath = Path.Combine(pmpWorkingDir,
                    normalizedGamePath.Replace('/', Path.DirectorySeparatorChar));
                var destDir = Path.GetDirectoryName(destFilePath);
                if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

                await Task.Run(() => File.Copy(sourceFilePath, destFilePath, true));
                defaultMod.Files.Add(normalizedGamePath, normalizedGamePath);
            } catch (Exception ex)
            {
                PluginLog.Error($"Failed to write file for PMP export: {normalizedGamePath}\n{ex}");
            }
        }

        defaultMod.Manipulations = ConvertPenumbraMeta(snapshotInfo.ManipulationString);
        return defaultMod;
    }

    private static List<PmpManipulationEntry> ConvertPenumbraMeta(string base64)
    {
        var list = new List<PmpManipulationEntry>();
        if (string.IsNullOrEmpty(base64) || !ConvertManips(base64, out var manips) || manips == null)
            return list;

        void AddManips<TId, TEntry>(string type, IReadOnlyDictionary<TId, TEntry> dict)
            where TId : unmanaged, IMetaIdentifier
            where TEntry : unmanaged
        {
            foreach (var (id, entry) in dict)
                list.Add(new PmpManipulationEntry
                {
                    Type = type,
                    // Added '!' here to resolve the CS8602 warning.
                    Manipulation = JObject.FromObject(MetaDictionary.Serialize(id, entry)!["Manipulation"]!)
                });
        }

        AddManips("Imc", manips.Imc);
        AddManips("Eqp", manips.Eqp);
        AddManips("Eqdp", manips.Eqdp);
        AddManips("Est", manips.Est);
        AddManips("Gmp", manips.Gmp);
        AddManips("Rsp", manips.Rsp);
        AddManips("Atch", manips.Atch);
        AddManips("Shp", manips.Shp);
        AddManips("Atr", manips.Atr);

        foreach (var identifier in manips.GlobalEqp)
            list.Add(new PmpManipulationEntry
            {
                Type = "GlobalEqp",
                Manipulation = JObject.FromObject(MetaDictionary.Serialize(identifier)["Manipulation"]!)
            });

        return list;
    }

    private static bool ConvertManips(string manipString, [NotNullWhen(true)] out MetaDictionary? manips)
    {
        if (manipString.Length == 0)
        {
            manips = new MetaDictionary();
            return true;
        }

        try
        {
            var bytes = Convert.FromBase64String(manipString);
            using var compressedStream = new MemoryStream(bytes);
            using var zipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var resultStream = new MemoryStream();
            zipStream.CopyTo(resultStream);
            resultStream.Flush();
            resultStream.Position = 0;
            var data = resultStream.GetBuffer().AsSpan(0, (int) resultStream.Length);
            var version = data[0];
            data = data[1..];
            switch (version)
            {
                case 0:
                    return ConvertManipsV0(data, out manips);
                case 1:
                    return ConvertManipsV1(data, out manips);
                default:
                    PluginLog.Warning($"Invalid version for manipulations: {version}.");
                    manips = null;
                    return false;
            }
        } catch (Exception ex)
        {
            PluginLog.Error($"Error decompressing manipulations:\n{ex}");
            manips = null;
            return false;
        }
    }

    private static bool ConvertManipsV0(ReadOnlySpan<byte> data, [NotNullWhen(true)] out MetaDictionary? manips)
    {
        var json = Encoding.UTF8.GetString(data);
        manips = JsonConvert.DeserializeObject<MetaDictionary>(json);
        return manips != null;
    }

    private static bool ConvertManipsV1(ReadOnlySpan<byte> data, [NotNullWhen(true)] out MetaDictionary? manips)
    {
        if (!data.StartsWith("META0001"u8))
        {
            PluginLog.Warning("Invalid manipulations of version 1, does not start with valid prefix.");
            manips = null;
            return false;
        }

        manips = new MetaDictionary();
        var r = new SpanBinaryReader(data[8..]);
        var imcCount = r.ReadInt32();
        for (var i = 0; i < imcCount; ++i)
        {
            var id = r.Read<ImcIdentifier>();
            var v = r.Read<ImcEntry>();
            if (!id.Validate() || !manips.TryAdd(id, v)) return false;
        }

        var eqpCount = r.ReadInt32();
        for (var i = 0; i < eqpCount; ++i)
        {
            var id = r.Read<EqpIdentifier>();
            var v = r.Read<EqpEntry>();
            if (!id.Validate() || !manips.TryAdd(id, v)) return false;
        }

        var eqdpCount = r.ReadInt32();
        for (var i = 0; i < eqdpCount; ++i)
        {
            var id = r.Read<EqdpIdentifier>();
            var v = r.Read<EqdpEntry>();
            if (!id.Validate() || !manips.TryAdd(id, v)) return false;
        }

        var estCount = r.ReadInt32();
        for (var i = 0; i < estCount; ++i)
        {
            var id = r.Read<EstIdentifier>();
            var v = r.Read<EstEntry>();
            if (!id.Validate() || !manips.TryAdd(id, v)) return false;
        }

        var rspCount = r.ReadInt32();
        for (var i = 0; i < rspCount; ++i)
        {
            var id = r.Read<RspIdentifier>();
            var v = r.Read<RspEntry>();
            if (!id.Validate() || !manips.TryAdd(id, v)) return false;
        }

        var gmpCount = r.ReadInt32();
        for (var i = 0; i < gmpCount; ++i)
        {
            var id = r.Read<GmpIdentifier>();
            var v = r.Read<GmpEntry>();
            if (!id.Validate() || !manips.TryAdd(id, v)) return false;
        }

        var globalEqpCount = r.ReadInt32();
        for (var i = 0; i < globalEqpCount; ++i)
        {
            var m = r.Read<GlobalEqpManipulation>();
            if (!m.Validate() || !manips.TryAdd(m)) return false;
        }

        if (r.Position < data.Length - 8)
        {
            var atchCount = r.ReadInt32();
            for (var i = 0; i < atchCount; ++i)
            {
                var id = r.Read<AtchIdentifier>();
                var v = r.Read<AtchEntry>();
                if (!id.Validate() || !manips.TryAdd(id, v)) return false;
            }
        }

        if (r.Position < data.Length - 8)
        {
            var shpCount = r.ReadInt32();
            for (var i = 0; i < shpCount; ++i)
            {
                var id = r.Read<ShpIdentifier>();
                var v = r.Read<ShpEntry>();
                if (!id.Validate() || !manips.TryAdd(id, v)) return false;
            }
        }

        if (r.Position < data.Length - 8)
        {
            var atrCount = r.ReadInt32();
            for (var i = 0; i < atrCount; ++i)
            {
                var id = r.Read<AtrIdentifier>();
                var v = r.Read<AtrEntry>();
                if (!id.Validate() || !manips.TryAdd(id, v)) return false;
            }
        }

        return true;
    }

    private ref struct SpanBinaryReader
    {
        private readonly ReadOnlySpan<byte> _span;
        public int Position { get; private set; }

        public SpanBinaryReader(ReadOnlySpan<byte> span)
        {
            _span = span;
            Position = 0;
        }

        public T Read<T>() where T : unmanaged
        {
            var size = Unsafe.SizeOf<T>();
            if (size > _span.Length - Position) throw new EndOfStreamException();

            var value = MemoryMarshal.Read<T>(_span.Slice(Position));
            Position += size;
            return value;
        }

        public int ReadInt32()
        {
            return Read<int>();
        }
    }
}
