namespace Snappy.Models;

public class GlamourerHistory
{
    public List<GlamourerHistoryEntry> Entries { get; set; } = new();
}

public record GlamourerHistoryEntry : HistoryEntryBase
{
    public string GlamourerString { get; set; } = string.Empty;

    public static GlamourerHistoryEntry Create(string glamourerString, string description)
    {
        return new GlamourerHistoryEntry
        {
            Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            Description = description,
            GlamourerString = glamourerString
        };
    }
}