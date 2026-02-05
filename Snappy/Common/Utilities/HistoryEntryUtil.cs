namespace Snappy.Common.Utilities;

public static class HistoryEntryUtil
{
    public static bool TryParseTimestamp(string? timestamp, out DateTime parsedUtc)
    {
        return DateTime.TryParse(timestamp, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out parsedUtc);
    }

    public static string FormatEntryPreview(HistoryEntryBase entry)
    {
        var time = entry.Timestamp;
        if (TryParseTimestamp(entry.Timestamp, out var parsedUtc))
            time = parsedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        var name = string.IsNullOrWhiteSpace(entry.Description) ? "Unnamed Entry" : entry.Description;
        return $"{name}  ({time})";
    }
}
