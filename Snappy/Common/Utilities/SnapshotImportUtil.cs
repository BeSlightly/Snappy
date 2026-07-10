using Luna;
using System.Threading;

namespace Snappy.Common.Utilities;

public static class SnapshotImportUtil
{
    private static readonly SemaphoreSlim ImportLock = new(1, 1);

    public static bool TryAcquireImportLock(out IDisposable? lease)
    {
        if (!ImportLock.Wait(0))
        {
            lease = null;
            return false;
        }

        lease = new ImportLockLease();
        return true;
    }

    public static SnapshotInfo BuildSnapshotInfo(string sourceActor, int? sourceWorldId, string manipulationString,
        Dictionary<string, string> fileReplacements, Dictionary<string, string>? fileSwaps = null)
    {
        var normalizedReplacements = GamePathUtil.NormalizeFileMap(fileReplacements);
        var normalizedSwaps = GamePathUtil.NormalizeFileSwaps(fileSwaps
                                                              ?? new Dictionary<string, string>());
        foreach (var gamePath in normalizedSwaps.Keys)
            normalizedReplacements.Remove(gamePath);

        var snapshotInfo = new SnapshotInfo
        {
            SourceActor = sourceActor,
            SourceWorldId = sourceWorldId,
            LastUpdate = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ManipulationString = manipulationString,
            FileReplacements = normalizedReplacements,
            FileSwaps = normalizedSwaps
        };

        if (snapshotInfo.FileReplacements.Any() || snapshotInfo.FileSwaps.Any())
        {
            var baseId = Guid.NewGuid().ToString("N");
            snapshotInfo.FileMaps.Add(new FileMapEntry
            {
                Id = baseId,
                BaseId = null,
                Changes = new Dictionary<string, string>(snapshotInfo.FileReplacements,
                    StringComparer.OrdinalIgnoreCase),
                FileSwapChanges = new Dictionary<string, string>(snapshotInfo.FileSwaps,
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

    private sealed class ImportLockLease : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                ImportLock.Release();
        }
    }
}
