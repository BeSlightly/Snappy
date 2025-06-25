using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Penumbra.GameData.Files.AtchStructs;
using Penumbra.GameData.Structs;
using Penumbra.Meta.Manipulations;
using Snappy.Features.Pmp.Models;
using Snappy.Models;

namespace Snappy.Features.Pmp
{
    public class PmpExportManager
    {
        private readonly Plugin _plugin;
        public bool IsExporting { get; private set; }

        public PmpExportManager(Plugin plugin)
        {
            _plugin = plugin;
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

            public T Read<T>()
                where T : unmanaged
            {
                var size = Unsafe.SizeOf<T>();
                if (size > _span.Length - Position)
                    throw new EndOfStreamException();

                var value = MemoryMarshal.Read<T>(_span.Slice(Position));
                Position += size;
                return value;
            }

            public int ReadInt32() => Read<int>();
        }

        private List<PmpManipulationEntry> ConvertPenumbraMeta(string base64)
        {
            var list = new List<PmpManipulationEntry>();
            if (string.IsNullOrEmpty(base64))
                return list;

            if (!ConvertManips(base64, out var manips, out _) || manips == null)
                return list;

            foreach (var (id, entry) in manips.Imc)
                list.Add(
                    new PmpManipulationEntry
                    {
                        Type = "Imc",
                        Manipulation = JObject.FromObject(
                            MetaDictionary.Serialize(id, entry)["Manipulation"]!
                        ),
                    }
                );
            foreach (var (id, entry) in manips.Eqp)
                list.Add(
                    new PmpManipulationEntry
                    {
                        Type = "Eqp",
                        Manipulation = JObject.FromObject(
                            MetaDictionary.Serialize(id, entry)["Manipulation"]!
                        ),
                    }
                );
            foreach (var (id, entry) in manips.Eqdp)
                list.Add(
                    new PmpManipulationEntry
                    {
                        Type = "Eqdp",
                        Manipulation = JObject.FromObject(
                            MetaDictionary.Serialize(id, entry)["Manipulation"]!
                        ),
                    }
                );
            foreach (var (id, entry) in manips.Est)
                list.Add(
                    new PmpManipulationEntry
                    {
                        Type = "Est",
                        Manipulation = JObject.FromObject(
                            MetaDictionary.Serialize(id, entry)["Manipulation"]!
                        ),
                    }
                );
            foreach (var (id, entry) in manips.Gmp)
                list.Add(
                    new PmpManipulationEntry
                    {
                        Type = "Gmp",
                        Manipulation = JObject.FromObject(
                            MetaDictionary.Serialize(id, entry)["Manipulation"]!
                        ),
                    }
                );
            foreach (var (id, entry) in manips.Rsp)
                list.Add(
                    new PmpManipulationEntry
                    {
                        Type = "Rsp",
                        Manipulation = JObject.FromObject(
                            MetaDictionary.Serialize(id, entry)["Manipulation"]!
                        ),
                    }
                );
            foreach (var (id, entry) in manips.Atch)
                list.Add(
                    new PmpManipulationEntry
                    {
                        Type = "Atch",
                        Manipulation = JObject.FromObject(
                            MetaDictionary.Serialize(id, entry)["Manipulation"]!
                        ),
                    }
                );
            foreach (var (id, entry) in manips.Shp)
                list.Add(
                    new PmpManipulationEntry
                    {
                        Type = "Shp",
                        Manipulation = JObject.FromObject(
                            MetaDictionary.Serialize(id, entry)["Manipulation"]!
                        ),
                    }
                );
            foreach (var (id, entry) in manips.Atr)
                list.Add(
                    new PmpManipulationEntry
                    {
                        Type = "Atr",
                        Manipulation = JObject.FromObject(
                            MetaDictionary.Serialize(id, entry)["Manipulation"]!
                        ),
                    }
                );
            foreach (var identifier in manips.GlobalEqp)
                list.Add(
                    new PmpManipulationEntry
                    {
                        Type = "GlobalEqp",
                        Manipulation = JObject.FromObject(
                            MetaDictionary.Serialize(identifier)["Manipulation"]!
                        ),
                    }
                );

            return list;
        }

        private bool ConvertManips(
            string manipString,
            [NotNullWhen(true)] out MetaDictionary? manips,
            out byte version
        )
        {
            if (manipString.Length == 0)
            {
                manips = new MetaDictionary();
                version = byte.MaxValue;
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
                var data = resultStream.GetBuffer().AsSpan(0, (int)resultStream.Length);
                version = data[0];
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
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Error decompressing manipulations:\n{ex}");
                manips = null;
                version = byte.MaxValue;
                return false;
            }
        }

        private bool ConvertManipsV0(
            ReadOnlySpan<byte> data,
            [NotNullWhen(true)] out MetaDictionary? manips
        )
        {
            var json = Encoding.UTF8.GetString(data);
            manips = JsonConvert.DeserializeObject<MetaDictionary>(json);
            return manips != null;
        }

        private bool ConvertManipsV1(
            ReadOnlySpan<byte> data,
            [NotNullWhen(true)] out MetaDictionary? manips
        )
        {
            if (!data.StartsWith("META0001"u8))
            {
                PluginLog.Warning(
                    $"Invalid manipulations of version 1, does not start with valid prefix."
                );
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
                if (!id.Validate() || !manips.TryAdd(id, v))
                    return false;
            }
            var eqpCount = r.ReadInt32();
            for (var i = 0; i < eqpCount; ++i)
            {
                var id = r.Read<EqpIdentifier>();
                var v = r.Read<EqpEntry>();
                if (!id.Validate() || !manips.TryAdd(id, v))
                    return false;
            }
            var eqdpCount = r.ReadInt32();
            for (var i = 0; i < eqdpCount; ++i)
            {
                var id = r.Read<EqdpIdentifier>();
                var v = r.Read<EqdpEntry>();
                if (!id.Validate() || !manips.TryAdd(id, v))
                    return false;
            }
            var estCount = r.ReadInt32();
            for (var i = 0; i < estCount; ++i)
            {
                var id = r.Read<EstIdentifier>();
                var v = r.Read<EstEntry>();
                if (!id.Validate() || !manips.TryAdd(id, v))
                    return false;
            }
            var rspCount = r.ReadInt32();
            for (var i = 0; i < rspCount; ++i)
            {
                var id = r.Read<RspIdentifier>();
                var v = r.Read<RspEntry>();
                if (!id.Validate() || !manips.TryAdd(id, v))
                    return false;
            }
            var gmpCount = r.ReadInt32();
            for (var i = 0; i < gmpCount; ++i)
            {
                var id = r.Read<GmpIdentifier>();
                var v = r.Read<GmpEntry>();
                if (!id.Validate() || !manips.TryAdd(id, v))
                    return false;
            }
            var globalEqpCount = r.ReadInt32();
            for (var i = 0; i < globalEqpCount; ++i)
            {
                var m = r.Read<GlobalEqpManipulation>();
                if (!m.Validate() || !manips.TryAdd(m))
                    return false;
            }
            if (r.Position < data.Length - 8)
            {
                var atchCount = r.ReadInt32();
                for (var i = 0; i < atchCount; ++i)
                {
                    var id = r.Read<AtchIdentifier>();
                    var v = r.Read<AtchEntry>();
                    if (!id.Validate() || !manips.TryAdd(id, v))
                        return false;
                }
            }
            if (r.Position < data.Length - 8)
            {
                var shpCount = r.ReadInt32();
                for (var i = 0; i < shpCount; ++i)
                {
                    var id = r.Read<ShpIdentifier>();
                    var v = r.Read<ShpEntry>();
                    if (!id.Validate() || !manips.TryAdd(id, v))
                        return false;
                }
            }
            if (r.Position < data.Length - 8)
            {
                var atrCount = r.ReadInt32();
                for (var i = 0; i < atrCount; ++i)
                {
                    var id = r.Read<AtrIdentifier>();
                    var v = r.Read<AtrEntry>();
                    if (!id.Validate() || !manips.TryAdd(id, v))
                        return false;
                }
            }
            return true;
        }

        public void SnapshotToPMP(string snapshotPath)
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

                string infoJsonPath = Path.Combine(snapshotPath, "snapshot.json");
                if (!File.Exists(infoJsonPath))
                {
                    Notify.Error("Export failed: snapshot.json not found.");
                    return;
                }
                SnapshotInfo? snapshotInfo = JsonConvert.DeserializeObject<SnapshotInfo>(
                    File.ReadAllText(infoJsonPath)
                );
                if (snapshotInfo == null)
                {
                    Notify.Error("Export failed: Could not read snapshot.json.");
                    return;
                }

                string snapshotName = new DirectoryInfo(snapshotPath).Name;
                string pmpFileName = $"{snapshotName}_{Guid.NewGuid():N}";
                string workingDirectory = Path.Combine(
                    _plugin.Configuration.WorkingDirectory,
                    $"temp_{pmpFileName}"
                );
                if (Directory.Exists(workingDirectory))
                    Directory.Delete(workingDirectory, true);
                Directory.CreateDirectory(workingDirectory);

                PmpMetadata metadata = new PmpMetadata
                {
                    Name = snapshotName,
                    Author = "Snapper",
                    Description = $"A snapshot of {snapshotInfo.SourceActor}.",
                };
                File.WriteAllText(
                    Path.Combine(workingDirectory, "meta.json"),
                    JsonConvert.SerializeObject(metadata, Formatting.Indented)
                );

                PmpDefaultMod defaultMod = new PmpDefaultMod();
                var sourceFilesDir = Path.Combine(snapshotPath, "_files");

                foreach (var (gamePath, hash) in snapshotInfo.FileReplacements)
                {
                    var normalizedGamePath = gamePath.Replace('\\', '/').TrimStart('/');
                    defaultMod.Files.Add(normalizedGamePath, normalizedGamePath);

                    var sourceFilePath = Path.Combine(sourceFilesDir, $"{hash}.dat");
                    var destFilePath = Path.Combine(
                        workingDirectory,
                        normalizedGamePath.Replace('/', Path.DirectorySeparatorChar)
                    );

                    if (!File.Exists(sourceFilePath))
                    {
                        PluginLog.Warning(
                            $"Missing file blob for {normalizedGamePath} (hash: {hash}). It will not be included in the PMP."
                        );
                        defaultMod.Files.Remove(normalizedGamePath);
                        continue;
                    }

                    try
                    {
                        var destDir = Path.GetDirectoryName(destFilePath);
                        if (!string.IsNullOrEmpty(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }
                        File.Copy(sourceFilePath, destFilePath, true);
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Error(
                            $"Failed to write file for PMP export: {normalizedGamePath}\n{ex}"
                        );
                        defaultMod.Files.Remove(normalizedGamePath);
                    }
                }

                defaultMod.Manipulations = ConvertPenumbraMeta(snapshotInfo.ManipulationString);
                File.WriteAllText(
                    Path.Combine(workingDirectory, "default_mod.json"),
                    JsonConvert.SerializeObject(defaultMod, Formatting.Indented)
                );

                var pmpOutputPath = Path.Combine(
                    _plugin.Configuration.WorkingDirectory,
                    $"{pmpFileName}.pmp"
                );
                ZipFile.CreateFromDirectory(workingDirectory, pmpOutputPath);

                Directory.Delete(workingDirectory, true);
                Notify.Success($"Successfully exported {snapshotName} to {pmpOutputPath}");
            }
            catch (Exception e)
            {
                Notify.Error($"PMP export failed: {e.Message}");
                PluginLog.Error($"PMP export failed: {e}");
            }
            finally
            {
                IsExporting = false;
            }
        }
    }
}
