namespace Snappy.Features.Pcp;

public interface IPcpManager
{
    void ImportPcp(string filePath);
    Task ExportPcp(string snapshotPath, string outputPath);

    Task ExportPcp(string snapshotPath, string outputPath, GlamourerHistoryEntry? selectedGlamourer,
        CustomizeHistoryEntry? selectedCustomize);

    Task ExportPcp(
        string snapshotPath,
        string outputPath,
        GlamourerHistoryEntry? selectedGlamourer,
        CustomizeHistoryEntry? selectedCustomize,
        string? playerNameOverride,
        int? homeWorldIdOverride
    );
}