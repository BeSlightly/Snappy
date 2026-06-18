namespace Snappy.Features.Mcdf;

public record McdfHeader(byte Version, McdfData CharaFileData)
{
    public static readonly byte CurrentVersion = 1;

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
        var chars = new string(reader.ReadChars(4));
        if (chars != "MCDF") throw new Exception("Not a Mare Chara File");

        var version = reader.ReadByte();
        if (version == 1)
        {
            var dataLength = reader.ReadInt32();
            return (version, dataLength);
        }

        throw new Exception($"Unsupported MCDF version: {version}");
    }

    public static McdfHeader FromBinaryReader(string path, BinaryReader reader)
    {
        var initialPosition = reader.BaseStream.Position;
        try
        {
            var (version, dataLength) = ReadHeader(reader);
            return new McdfHeader(version, McdfData.FromByteArray(reader.ReadBytes(dataLength)));
        }
        catch (Exception)
        {
            reader.BaseStream.Position = initialPosition;
            throw;
        }
    }
}
