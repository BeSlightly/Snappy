namespace Snappy.Features.Mcdf;

public record McdfHeader(byte Version, McdfData CharaFileData)
{
    public static readonly byte CurrentVersion = 1;
    private const int MaxHeaderDataLength = 32 * 1024 * 1024;

    public byte Version { get; set; } = Version;
    public McdfData CharaFileData { get; set; } = CharaFileData;

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

    private static (byte Version, int DataLength) ReadHeader(BinaryReader reader)
    {
        var magic = reader.ReadBytes(4);
        if (magic.Length != 4 || !magic.SequenceEqual("MCDF"u8.ToArray()))
            throw new InvalidDataException("Not a Mare Chara File");

        var version = reader.ReadByte();
        if (version == 1)
        {
            var dataLength = reader.ReadInt32();
            if (dataLength is < 0 or > MaxHeaderDataLength)
                throw new InvalidDataException($"Invalid MCDF header length: {dataLength}.");
            return (version, dataLength);
        }

        throw new InvalidDataException($"Unsupported MCDF version: {version}");
    }

    public static McdfHeader FromBinaryReader(BinaryReader reader)
    {
        var (version, dataLength) = ReadHeader(reader);
        var data = reader.ReadBytes(dataLength);
        if (data.Length != dataLength)
            throw new EndOfStreamException("MCDF header ended before all metadata could be read.");

        return new McdfHeader(version, McdfData.FromByteArray(data));
    }
}
