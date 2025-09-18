using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Penumbra.GameData.Files.AtchStructs;
using Penumbra.GameData.Structs;
using Penumbra.Meta.Manipulations;

namespace Snappy.Common.Utilities;

public static class PenumbraMetaUtil
{
    public static List<JObject> ConvertPenumbraMetaToJObjects(string base64)
    {
        var list = new List<JObject>();
        if (string.IsNullOrEmpty(base64) || !ConvertManips(base64, out var manips) || manips == null)
            return list;

        void AddManips<TId, TEntry>(IReadOnlyDictionary<TId, TEntry> dict)
            where TId : unmanaged, IMetaIdentifier
            where TEntry : unmanaged
        {
            foreach (var (id, entry) in dict)
            {
                // CORRECTED: Get the entire manipulation object, not just the inner part.
                var manipulationObject = MetaDictionary.Serialize(id, entry);
                if (manipulationObject != null)
                    list.Add(manipulationObject);
            }
        }

        AddManips(manips.Imc);
        AddManips(manips.Eqp);
        AddManips(manips.Eqdp);
        AddManips(manips.Est);
        AddManips(manips.Gmp);
        AddManips(manips.Rsp);
        AddManips(manips.Atch);
        AddManips(manips.Shp);
        AddManips(manips.Atr);

        foreach (var identifier in manips.GlobalEqp)
        {
            // CORRECTED: Get the entire manipulation object.
            var manipulationObject = MetaDictionary.Serialize(identifier);
            if (manipulationObject != null)
                list.Add(manipulationObject);
        }

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
            var data = resultStream.GetBuffer().AsSpan(0, (int)resultStream.Length);
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
        }
        catch
        {
            PluginLog.Error("Error decompressing manipulations");
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