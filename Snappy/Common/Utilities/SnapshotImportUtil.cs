using Luna;

namespace Snappy.Common.Utilities;

public static class SnapshotImportUtil
{
    public static SnapshotInfo BuildSnapshotInfo(string sourceActor, int? sourceWorldId, string manipulationString,
        Dictionary<string, string> fileReplacements)
    {
        var snapshotInfo = new SnapshotInfo
        {
            SourceActor = sourceActor,
            SourceWorldId = sourceWorldId,
            LastUpdate = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ManipulationString = manipulationString,
            FileReplacements = fileReplacements
        };

        if (snapshotInfo.FileReplacements.Any())
        {
            var baseId = Guid.NewGuid().ToString("N");
            snapshotInfo.FileMaps.Add(new FileMapEntry
            {
                Id = baseId,
                BaseId = null,
                Changes = new Dictionary<string, string>(snapshotInfo.FileReplacements,
                    StringComparer.OrdinalIgnoreCase),
                Timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                ManipulationString = snapshotInfo.ManipulationString
            });
            snapshotInfo.CurrentFileMapId = baseId;
        }

        return snapshotInfo;
    }

    public static string CreateUniqueSnapshotDirectory(string workingDirectory, string directoryName,
        Action<string>? onConflict = null)
    {
        var sanitizedName = PathSanitizer.SanitizeFileSystemName(
            directoryName,
            $"Snapshot_{DateTime.Now:yyyyMMddHHmmss}");
        var snapshotPath = Path.CombineSafely(workingDirectory, sanitizedName);

        var counter = 1;
        var originalName = sanitizedName;
        while (Directory.Exists(snapshotPath))
        {
            var candidateName = $"{originalName}_{counter}";
            onConflict?.Invoke(candidateName);
            snapshotPath = Path.CombineSafely(workingDirectory, candidateName);
            counter++;
        }

        Directory.CreateDirectory(snapshotPath);
        return snapshotPath;
    }

    public static string SanitizeDirectoryName(string? name, string fallbackPrefix)
    {
        var fallback = $"{fallbackPrefix}_{DateTime.Now:yyyyMMddHHmmss}";
        return PathSanitizer.SanitizeFileSystemName(name, fallback);
    }
}
