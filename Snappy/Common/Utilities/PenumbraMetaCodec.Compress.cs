using System.IO.Compression;
using Penumbra.GameData.Files.AtchStructs;
using Penumbra.GameData.Files.Utility;
using Penumbra.GameData.Structs;
using Penumbra.Meta.Manipulations;

namespace Snappy.Common.Utilities;

public static partial class PenumbraMetaCodec
{
    public static string CompressV1(MetaDictionary manips)
    {
        using var ms = new MemoryStream();
        using (var zipStream = new GZipStream(ms, CompressionMode.Compress, true))
        {
            zipStream.Write((byte)1);
            zipStream.Write("META0001"u8);

            WriteCache(zipStream, manips.Imc);
            WriteCache(zipStream, manips.Eqp);
            WriteCache(zipStream, manips.Eqdp);
            WriteCache(zipStream, manips.Est);
            WriteCache(zipStream, manips.Rsp);
            WriteCache(zipStream, manips.Gmp);

            zipStream.Write(manips.GlobalEqp.Count);
            foreach (var geqp in manips.GlobalEqp)
                zipStream.Write(geqp);

            WriteCache(zipStream, manips.Atch);
            WriteCache(zipStream, manips.Shp);
            WriteCache(zipStream, manips.Atr);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    private static void WriteCache(Stream stream, IReadOnlyDictionary<ImcIdentifier, ImcEntry> data)
    {
        stream.Write(data.Count);
        foreach (var (id, entry) in data)
        {
            stream.Write(id);
            stream.Write(entry);
        }
    }

    private static void WriteCache(Stream stream, IReadOnlyDictionary<EqpIdentifier, EqpEntryInternal> data)
    {
        stream.Write(data.Count);
        foreach (var (id, entry) in data)
        {
            stream.Write(id);
            stream.Write(entry.ToEntry(id.Slot));
        }
    }

    private static void WriteCache(Stream stream, IReadOnlyDictionary<EqdpIdentifier, EqdpEntryInternal> data)
    {
        stream.Write(data.Count);
        foreach (var (id, entry) in data)
        {
            stream.Write(id);
            stream.Write(entry.ToEntry(id.Slot));
        }
    }

    private static void WriteCache(Stream stream, IReadOnlyDictionary<EstIdentifier, EstEntry> data)
    {
        stream.Write(data.Count);
        foreach (var (id, entry) in data)
        {
            stream.Write(id);
            stream.Write(entry);
        }
    }

    private static void WriteCache(Stream stream, IReadOnlyDictionary<RspIdentifier, RspEntry> data)
    {
        stream.Write(data.Count);
        foreach (var (id, entry) in data)
        {
            stream.Write(id);
            stream.Write(entry);
        }
    }

    private static void WriteCache(Stream stream, IReadOnlyDictionary<GmpIdentifier, GmpEntry> data)
    {
        stream.Write(data.Count);
        foreach (var (id, entry) in data)
        {
            stream.Write(id);
            stream.Write(entry);
        }
    }

    private static void WriteCache(Stream stream, IReadOnlyDictionary<AtchIdentifier, AtchEntry> data)
    {
        stream.Write(data.Count);
        foreach (var (id, entry) in data)
        {
            stream.Write(id);
            stream.Write(entry);
        }
    }

    private static void WriteCache(Stream stream, IReadOnlyDictionary<ShpIdentifier, ShpEntry> data)
    {
        stream.Write(data.Count);
        foreach (var (id, entry) in data)
        {
            stream.Write(id);
            stream.Write(entry);
        }
    }

    private static void WriteCache(Stream stream, IReadOnlyDictionary<AtrIdentifier, AtrEntry> data)
    {
        stream.Write(data.Count);
        foreach (var (id, entry) in data)
        {
            stream.Write(id);
            stream.Write(entry);
        }
    }
}
