namespace Snappy.Models;

public record SnapshotInfo
{
    public int FormatVersion { get; set; } = 1;
    public string SourceActor { get; set; } = string.Empty;
    public int? SourceWorldId { get; set; }
    public string LastUpdate { get; set; } = string.Empty;

    public Dictionary<string, string> FileReplacements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string ManipulationString { get; set; } = string.Empty;
}