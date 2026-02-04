namespace Snappy.Features.Pmp;

public interface IPmpExportManager
{
    bool IsExporting { get; }
    Task SnapshotToPMPAsync(string snapshotPath, string? outputPath = null, string? fileMapId = null);
    Task SnapshotToPMPAsync(string snapshotPath, string? outputPath, string? fileMapId,
        IReadOnlyDictionary<string, string>? fileMapOverride, string? manipulationOverride);
}
