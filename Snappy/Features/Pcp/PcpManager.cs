using Snappy.Services.SnapshotManager;

namespace Snappy.Features.Pcp;

public class PcpManager : IPcpManager
{
    private readonly PcpImportService _importService;
    private readonly PcpExportService _exportService;

    public PcpManager(Configuration configuration, ISnapshotFileService snapshotFileService,
        Action snapshotsUpdatedCallback)
    {
        _importService = new PcpImportService(configuration, snapshotFileService, snapshotsUpdatedCallback);
        _exportService = new PcpExportService();
    }

    public void ImportPcp(string filePath)
    {
        _importService.ImportPcp(filePath);
    }

    public Task ExportPcp(string snapshotPath, string outputPath)
    {
        return _exportService.ExportPcp(snapshotPath, outputPath);
    }

    public Task ExportPcp(string snapshotPath, string outputPath, GlamourerHistoryEntry? selectedGlamourer,
        CustomizeHistoryEntry? selectedCustomize)
    {
        return _exportService.ExportPcp(snapshotPath, outputPath, selectedGlamourer, selectedCustomize);
    }

    public Task ExportPcp(
        string snapshotPath,
        string outputPath,
        GlamourerHistoryEntry? selectedGlamourer,
        CustomizeHistoryEntry? selectedCustomize,
        string? playerNameOverride,
        int? homeWorldIdOverride)
    {
        return _exportService.ExportPcp(snapshotPath, outputPath, selectedGlamourer, selectedCustomize,
            playerNameOverride, homeWorldIdOverride);
    }
}
