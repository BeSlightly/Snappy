using Penumbra.GameData.Files.AtchStructs;
using Penumbra.GameData.Structs;
using Penumbra.Meta.Manipulations;

namespace Snappy.Common.Utilities;

public static partial class PenumbraMetaCodec
{
    private const uint ImcKey = ((uint)'I' << 24) | ((uint)'M' << 16) | ((uint)'C' << 8);
    private const uint EqpKey = ((uint)'E' << 24) | ((uint)'Q' << 16) | ((uint)'P' << 8);
    private const uint EqdpKey = ((uint)'E' << 24) | ((uint)'Q' << 16) | ((uint)'D' << 8) | 'P';
    private const uint EstKey = ((uint)'E' << 24) | ((uint)'S' << 16) | ((uint)'T' << 8);
    private const uint RspKey = ((uint)'R' << 24) | ((uint)'S' << 16) | ((uint)'P' << 8);
    private const uint GmpKey = ((uint)'G' << 24) | ((uint)'M' << 16) | ((uint)'P' << 8);
    private const uint GeqpKey = ((uint)'G' << 24) | ((uint)'E' << 16) | ((uint)'Q' << 8) | 'P';
    private const uint AtchKey = ((uint)'A' << 24) | ((uint)'T' << 16) | ((uint)'C' << 8) | 'H';
    private const uint ShpKey = ((uint)'S' << 24) | ((uint)'H' << 16) | ((uint)'P' << 8);
    private const uint AtrKey = ((uint)'A' << 24) | ((uint)'T' << 16) | ((uint)'R' << 8);

    private static bool ConvertManipsV2(ReadOnlySpan<byte> data, out MetaDictionary? manips)
    {
        if (!data.StartsWith("META0002"u8))
        {
            manips = null;
            return false;
        }

        manips = new MetaDictionary();
        var r = new SpanBinaryReader(data[8..]);
        while (r.Remaining > 4)
        {
            var prefix = r.ReadUInt32();
            var count = r.Remaining > 4 ? r.ReadInt32() : 0;
            if (count == 0)
                continue;

            switch (prefix)
            {
                case ImcKey:
                    for (var i = 0; i < count; ++i)
                    {
                        var id = r.Read<ImcIdentifier>();
                        var v = r.Read<ImcEntry>();
                        if (!id.Validate() || !manips.TryAdd(id, v)) return false;
                    }
                    break;
                case EqpKey:
                    for (var i = 0; i < count; ++i)
                    {
                        var id = r.Read<EqpIdentifier>();
                        var v = r.Read<EqpEntry>();
                        if (!id.Validate() || !manips.TryAdd(id, v)) return false;
                    }
                    break;
                case EqdpKey:
                    for (var i = 0; i < count; ++i)
                    {
                        var id = r.Read<EqdpIdentifier>();
                        var v = r.Read<EqdpEntry>();
                        if (!id.Validate() || !manips.TryAdd(id, v)) return false;
                    }
                    break;
                case EstKey:
                    for (var i = 0; i < count; ++i)
                    {
                        var id = r.Read<EstIdentifier>();
                        var v = r.Read<EstEntry>();
                        if (!id.Validate() || !manips.TryAdd(id, v)) return false;
                    }
                    break;
                case RspKey:
                    for (var i = 0; i < count; ++i)
                    {
                        var id = r.Read<RspIdentifier>();
                        var v = r.Read<RspEntry>();
                        if (!id.Validate() || !manips.TryAdd(id, v)) return false;
                    }
                    break;
                case GmpKey:
                    for (var i = 0; i < count; ++i)
                    {
                        var id = r.Read<GmpIdentifier>();
                        var v = r.Read<GmpEntry>();
                        if (!id.Validate() || !manips.TryAdd(id, v)) return false;
                    }
                    break;
                case GeqpKey:
                    for (var i = 0; i < count; ++i)
                    {
                        var geqp = r.Read<GlobalEqpManipulation>();
                        if (!geqp.Validate() || !manips.TryAdd(geqp)) return false;
                    }
                    break;
                case AtchKey:
                    for (var i = 0; i < count; ++i)
                    {
                        var id = r.Read<AtchIdentifier>();
                        var v = r.Read<AtchEntry>();
                        if (!id.Validate() || !manips.TryAdd(id, v)) return false;
                    }
                    break;
                case ShpKey:
                    for (var i = 0; i < count; ++i)
                    {
                        var id = r.Read<ShpIdentifier>();
                        var v = r.Read<ShpEntry>();
                        if (!id.Validate() || !manips.TryAdd(id, v)) return false;
                    }
                    break;
                case AtrKey:
                    for (var i = 0; i < count; ++i)
                    {
                        var id = r.Read<AtrIdentifier>();
                        var v = r.Read<AtrEntry>();
                        if (!id.Validate() || !manips.TryAdd(id, v)) return false;
                    }
                    break;
            }
        }

        return true;
    }
}
