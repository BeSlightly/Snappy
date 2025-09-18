namespace Snappy.Features.Pmp;

public interface IPmpExportManager
{
    bool IsExporting { get; }
    Task SnapshotToPMPAsync(string snapshotPath);
}