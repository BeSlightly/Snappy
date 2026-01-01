using System;
using System.Collections.Generic;

namespace Snappy.Models;

public record SnapshotInfo
{
    public int FormatVersion { get; set; } = 1;
    public string SourceActor { get; set; } = string.Empty;
    public int? SourceWorldId { get; set; }
    public string? SourceWorldName { get; set; }
    public string LastUpdate { get; set; } = string.Empty;

    public Dictionary<string, string> FileReplacements { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<FileMapEntry> FileMaps { get; set; } = new();
    public string? CurrentFileMapId { get; set; }
    public string ManipulationString { get; set; } = string.Empty;
}
