namespace Snappy.Features.Mcdf;

public interface IMcdfManager
{
    void ImportMcdf(string filePath);
    Task ExportMcdf(string snapshotPath, string outputPath, GlamourerHistoryEntry? selectedGlamourer,
        CustomizeHistoryEntry? selectedCustomize);
}
