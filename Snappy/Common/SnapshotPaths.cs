using Snappy.Common.Utilities;

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

    public string GetLegacyHashedFilePath(string hash)
    {
        return Path.Combine(FilesDirectory, hash + Constants.DataFileExtension);
    }

    public string GetPreferredHashedFilePath(string hash, string gamePath)
    {
        return SnapshotBlobUtil.GetPreferredBlobPath(FilesDirectory, hash, gamePath);
    }

    public string? FindAnyExistingHashedFilePath(string hash)
    {
        return SnapshotBlobUtil.FindAnyExistingBlobPath(FilesDirectory, hash);
    }

    public string ResolveHashedFilePath(string hash, string gamePath)
    {
        return SnapshotBlobUtil.ResolveBlobPath(FilesDirectory, hash, gamePath);
    }
}
