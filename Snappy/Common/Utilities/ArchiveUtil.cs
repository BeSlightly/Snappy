using System.IO.Compression;

namespace Snappy.Common.Utilities;

public static class ArchiveUtil
{
    public static T? ReadJsonEntry<T>(
        ZipArchive archive,
        string entryName,
        Action<string> onError,
        string? missingMessage = null,
        string? parseFailureMessage = null)
    {
        var entry = archive.GetEntry(entryName);
        if (entry == null)
        {
            onError(missingMessage ?? $"Missing archive entry: {entryName}");
            return default;
        }

        using var entryStream = entry.Open();
        using var entryReader = new StreamReader(entryStream);
        var entryJson = entryReader.ReadToEnd();

        try
        {
            return JsonConvert.DeserializeObject<T>(entryJson);
        }
        catch (Exception ex)
        {
            onError(parseFailureMessage ?? $"Failed to parse archive entry: {entryName}");
            PluginLog.Error($"Failed to parse archive entry {entryName}: {ex}");
            return default;
        }
    }

    public static void WriteJsonEntry(ZipArchive archive, string entryName, object data)
    {
        var entry = archive.CreateEntry(entryName);
        using var streamWriter = new StreamWriter(entry.Open());
        var payload = JsonConvert.SerializeObject(data, Formatting.Indented);
        streamWriter.Write(payload);
    }
}
