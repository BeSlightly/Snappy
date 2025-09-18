namespace Snappy.Common;

public class SnapshotPaths
{
    private SnapshotPaths(string snapshotPath)
    {
        RootPath = snapshotPath;
        SnapshotFile = Path.Combine(snapshotPath, Constants.SnapshotFileName);
        GlamourerHistoryFile = Path.Combine(snapshotPath, Constants.GlamourerHistoryFileName);
        CustomizeHistoryFile = Path.Combine(snapshotPath, Constants.CustomizeHistoryFileName);
        FilesDirectory = Path.Combine(snapshotPath, Constants.FilesSubdirectory);
        MigrationMarker = Path.Combine(snapshotPath, Constants.MigrationMarkerFileName);
    }

    public string RootPath { get; }
    public string SnapshotFile { get; }
    public string GlamourerHistoryFile { get; }
    public string CustomizeHistoryFile { get; }
    public string FilesDirectory { get; }
    public string MigrationMarker { get; }

    public static SnapshotPaths From(string snapshotPath)
    {
        return new SnapshotPaths(snapshotPath);
    }

    public string GetHashedFilePath(string hash)
    {
        return Path.Combine(FilesDirectory, hash + Constants.DataFileExtension);
    }
}