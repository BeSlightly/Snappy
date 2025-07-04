﻿using System.IO;

namespace MareSynchronos.Export;

public record MareCharaFileHeader(byte Version, MareCharaFileData CharaFileData)
{
    public static readonly byte CurrentVersion = 1;

    public byte Version { get; set; } = Version;
    public MareCharaFileData CharaFileData { get; set; } = CharaFileData;
    public string FilePath { get; private set; }

    public void WriteToStream(BinaryWriter writer)
    {
        writer.Write('M');
        writer.Write('C');
        writer.Write('D');
        writer.Write('F');
        writer.Write(Version);
        var charaFileDataArray = CharaFileData.ToByteArray();
        writer.Write(charaFileDataArray.Length);
        writer.Write(charaFileDataArray);
    }

    public static MareCharaFileHeader? FromBinaryReader(string path, BinaryReader reader)
    {
        var chars = new string(reader.ReadChars(4));
        if (!string.Equals(chars, "MCDF", System.StringComparison.Ordinal)) throw new System.Exception("Not a Mare Chara File");

        MareCharaFileHeader? decoded = null;

        var version = reader.ReadByte();
        if (version == 1)
        {
            var dataLength = reader.ReadInt32();

            decoded = new(version, MareCharaFileData.FromByteArray(reader.ReadBytes(dataLength)));
            decoded.FilePath = path;
        }

        return decoded;
    }

    public void AdvanceReaderToData(BinaryReader reader)
    {
        reader.ReadChars(4);
        var version = reader.ReadByte();
        if (version == 1)
        {
            var length = reader.ReadInt32();
            _ = reader.ReadBytes(length);
        }
    }
}
