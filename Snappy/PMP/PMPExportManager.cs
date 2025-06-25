using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Penumbra.GameData.Files.AtchStructs;
using Penumbra.GameData.Structs;
using Penumbra.Meta.Manipulations;
using Snappy.Models;
using Snappy.Utils;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Snappy.PMP
{
    public class PMPExportManager
    {
        private Plugin plugin;
        public PMPExportManager(Plugin plugin)
        {
            this.plugin = plugin;
        }

        /// <summary>
        /// A helper struct to read unmanaged types from a span of bytes, similar to a BinaryReader.
        /// </summary>
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
                if (size > _span.Length - Position)
                    throw new EndOfStreamException();

                var value = MemoryMarshal.Read<T>(_span.Slice(Position));
                Position += size;
                return value;
            }

            public int ReadInt32() => Read<int>();
        }

        // This function and its helpers are adapted from Penumbra's MetaApi.cs (private methods)
        // to decompress and deserialize the meta manipulation data.
        private List<PMPManipulationEntry> ConvertPenumbraMeta(string base64)
        {
            var list = new List<PMPManipulationEntry>();
            if (string.IsNullOrEmpty(base64))
                return list;

            if (!ConvertManips(base64, out var manips, out _))
                return list;

            if (manips == null)
                return list;

            // Convert MetaDictionary to List<PMPManipulationEntry>
            foreach (var (identifier, entry) in manips.Imc)
                list.Add(new PMPManipulationEntry { Type = "Imc", Manipulation = JObject.FromObject(MetaDictionary.Serialize(identifier, entry)["Manipulation"]!) });
            foreach (var (identifier, entry) in manips.Eqp)
                list.Add(new PMPManipulationEntry { Type = "Eqp", Manipulation = JObject.FromObject(MetaDictionary.Serialize(identifier, entry)["Manipulation"]!) });
            foreach (var (identifier, entry) in manips.Eqdp)
                list.Add(new PMPManipulationEntry { Type = "Eqdp", Manipulation = JObject.FromObject(MetaDictionary.Serialize(identifier, entry)["Manipulation"]!) });
            foreach (var (identifier, entry) in manips.Est)
                list.Add(new PMPManipulationEntry { Type = "Est", Manipulation = JObject.FromObject(MetaDictionary.Serialize(identifier, entry)["Manipulation"]!) });
            foreach (var (identifier, entry) in manips.Gmp)
                list.Add(new PMPManipulationEntry { Type = "Gmp", Manipulation = JObject.FromObject(MetaDictionary.Serialize(identifier, entry)["Manipulation"]!) });
            foreach (var (identifier, entry) in manips.Rsp)
                list.Add(new PMPManipulationEntry { Type = "Rsp", Manipulation = JObject.FromObject(MetaDictionary.Serialize(identifier, entry)["Manipulation"]!) });
            foreach (var (identifier, entry) in manips.Atch)
                list.Add(new PMPManipulationEntry { Type = "Atch", Manipulation = JObject.FromObject(MetaDictionary.Serialize(identifier, entry)["Manipulation"]!) });
            foreach (var (identifier, entry) in manips.Shp)
                list.Add(new PMPManipulationEntry { Type = "Shp", Manipulation = JObject.FromObject(MetaDictionary.Serialize(identifier, entry)["Manipulation"]!) });
            foreach (var (identifier, entry) in manips.Atr)
                list.Add(new PMPManipulationEntry { Type = "Atr", Manipulation = JObject.FromObject(MetaDictionary.Serialize(identifier, entry)["Manipulation"]!) });
            foreach (var identifier in manips.GlobalEqp)
                list.Add(new PMPManipulationEntry { Type = "GlobalEqp", Manipulation = JObject.FromObject(MetaDictionary.Serialize(identifier)["Manipulation"]!) });

            return list;
        }

        private bool ConvertManips(string manipString, [NotNullWhen(true)] out MetaDictionary? manips, out byte version)
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
                    case 0: return ConvertManipsV0(data, out manips);
                    case 1: return ConvertManipsV1(data, out manips);
                    default:
                        Logger.Warn($"Invalid version for manipulations: {version}.");
                        manips = null;
                        return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error decompressing manipulations:\n{ex}");
                manips = null;
                version = byte.MaxValue;
                return false;
            }
        }

        private bool ConvertManipsV0(ReadOnlySpan<byte> data, [NotNullWhen(true)] out MetaDictionary? manips)
        {
            var json = Encoding.UTF8.GetString(data);
            manips = JsonConvert.DeserializeObject<MetaDictionary>(json);
            return manips != null;
        }

        private bool ConvertManipsV1(ReadOnlySpan<byte> data, [NotNullWhen(true)] out MetaDictionary? manips)
        {
            if (!data.StartsWith("META0001"u8))
            {
                Logger.Warn($"Invalid manipulations of version 1, does not start with valid prefix.");
                manips = null;
                return false;
            }

            manips = new MetaDictionary();
            var r = new SpanBinaryReader(data[8..]);
            var imcCount = r.ReadInt32(); for (var i = 0; i < imcCount; ++i) { var id = r.Read<ImcIdentifier>(); var v = r.Read<ImcEntry>(); if (!id.Validate() || !manips.TryAdd(id, v)) return false; }
            var eqpCount = r.ReadInt32(); for (var i = 0; i < eqpCount; ++i) { var id = r.Read<EqpIdentifier>(); var v = r.Read<EqpEntry>(); if (!id.Validate() || !manips.TryAdd(id, v)) return false; }
            var eqdpCount = r.ReadInt32(); for (var i = 0; i < eqdpCount; ++i) { var id = r.Read<EqdpIdentifier>(); var v = r.Read<EqdpEntry>(); if (!id.Validate() || !manips.TryAdd(id, v)) return false; }
            var estCount = r.ReadInt32(); for (var i = 0; i < estCount; ++i) { var id = r.Read<EstIdentifier>(); var v = r.Read<EstEntry>(); if (!id.Validate() || !manips.TryAdd(id, v)) return false; }
            var rspCount = r.ReadInt32(); for (var i = 0; i < rspCount; ++i) { var id = r.Read<RspIdentifier>(); var v = r.Read<RspEntry>(); if (!id.Validate() || !manips.TryAdd(id, v)) return false; }
            var gmpCount = r.ReadInt32(); for (var i = 0; i < gmpCount; ++i) { var id = r.Read<GmpIdentifier>(); var v = r.Read<GmpEntry>(); if (!id.Validate() || !manips.TryAdd(id, v)) return false; }
            var globalEqpCount = r.ReadInt32(); for (var i = 0; i < globalEqpCount; ++i) { var m = r.Read<GlobalEqpManipulation>(); if (!m.Validate() || !manips.TryAdd(m)) return false; }
            if (r.Position < data.Length - 8) { var atchCount = r.ReadInt32(); for (var i = 0; i < atchCount; ++i) { var id = r.Read<AtchIdentifier>(); var v = r.Read<AtchEntry>(); if (!id.Validate() || !manips.TryAdd(id, v)) return false; } }
            if (r.Position < data.Length - 8) { var shpCount = r.ReadInt32(); for (var i = 0; i < shpCount; ++i) { var id = r.Read<ShpIdentifier>(); var v = r.Read<ShpEntry>(); if (!id.Validate() || !manips.TryAdd(id, v)) return false; } }
            if (r.Position < data.Length - 8) { var atrCount = r.ReadInt32(); for (var i = 0; i < atrCount; ++i) { var id = r.Read<AtrIdentifier>(); var v = r.Read<AtrEntry>(); if (!id.Validate() || !manips.TryAdd(id, v)) return false; } }
            return true;
        }

        public void SnapshotToPMP(string snapshotPath)
        {
            Logger.Debug($"Operating on {snapshotPath}");
            //read snapshot
            string infoJson = File.ReadAllText(Path.Combine(snapshotPath, "snapshot.json"));
            if (infoJson == null)
            {
                Logger.Warn("No snapshot json found, aborting");
                return;
            }
            SnapshotInfo? snapshotInfo = System.Text.Json.JsonSerializer.Deserialize<SnapshotInfo>(infoJson);
            if (snapshotInfo == null)
            {
                Logger.Warn("Failed to deserialize snapshot json, aborting");
                return;
            }

            //begin building PMP
            string snapshotName = new DirectoryInfo(snapshotPath).Name;
            string pmpFileName = $"{snapshotName}_{Guid.NewGuid()}";


            string workingDirectory = Path.Combine(plugin.Configuration.WorkingDirectory, $"temp_{pmpFileName}");
            if (!Directory.Exists(workingDirectory))
            {
                Directory.CreateDirectory(workingDirectory);
            }

            //meta.json
            PMPMetadata metadata = new PMPMetadata();
            metadata.Name = snapshotName;
            metadata.Author = $"SnapperFork";
            using (FileStream stream = new FileStream(Path.Combine(workingDirectory, "meta.json"), FileMode.Create))
            {
                JsonSerializer.Serialize(stream, metadata);
            }

            //default_mod.json
            PMPDefaultMod defaultMod = new PMPDefaultMod();
            foreach (var file in snapshotInfo.FileReplacements)
            {
                foreach (var replacement in file.Value)
                {
                    defaultMod.Files.Add(replacement, file.Key);
                }
            }

            defaultMod.Manipulations = ConvertPenumbraMeta(snapshotInfo.ManipulationString);
            var defaultModJson = JsonConvert.SerializeObject(defaultMod, Formatting.Indented);
            File.WriteAllText(Path.Combine(workingDirectory, "default_mod.json"), defaultModJson);

            //mods
            foreach (var file in snapshotInfo.FileReplacements)
            {

                string modPath = Path.Combine(snapshotPath, file.Key);
                string destPath = Path.Combine(workingDirectory, file.Key);
                Logger.Debug($"Copying {modPath}");
                Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? "");
                File.Copy(modPath, destPath);
            }

            //zip and make pmp file
            ZipFile.CreateFromDirectory(workingDirectory, Path.Combine(plugin.Configuration.WorkingDirectory, $"{pmpFileName}.pmp"));

            //cleanup
            Directory.Delete(workingDirectory, true);
        }
    }
}