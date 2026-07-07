namespace Snappy.Models;

public record FileMapEntry
{
    public string Id { get; set; } = string.Empty;
    public string? BaseId { get; set; }
    public Dictionary<string, string> Changes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FileSwapChanges { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string Timestamp { get; set; } = string.Empty;
    public string? ManipulationString { get; set; }
}
