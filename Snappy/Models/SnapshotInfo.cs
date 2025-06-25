using System.Collections.Generic;

namespace Snappy.Models
{
    public class SnapshotInfo
    {
        public int FormatVersion { get; set; } = 1;
        public string SourceActor { get; set; } = string.Empty;
        public string LastUpdate { get; set; } = string.Empty;
        public Dictionary<string, string> FileReplacements { get; set; } =
            new(System.StringComparer.OrdinalIgnoreCase);
        public string ManipulationString { get; set; } = string.Empty;
    }
}
