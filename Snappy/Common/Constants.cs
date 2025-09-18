using Penumbra.GameData.Structs;

namespace Snappy.Common;

public static class Constants
{
    // Snapshot File & Directory Names
    public const string SnapshotFileName = "snapshot.json";
    public const string GlamourerHistoryFileName = "glamourer_history.json";
    public const string CustomizeHistoryFileName = "customize_history.json";
    public const string FilesSubdirectory = "_files";
    public const string DataFileExtension = ".dat";
    public const string MigrationMarkerFileName = ".migrated";

    // IPC
    public const uint GlamourerLockKey = 0x534E4150; // "SNAP"
}