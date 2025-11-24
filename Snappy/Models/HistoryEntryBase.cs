namespace Snappy.Models;

public abstract record HistoryEntryBase
{
    public string Timestamp { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? FileMapId { get; set; }
}
