namespace Snappy.Services.SnapshotManager;

public sealed record HistoryEntryDeletionResult(
    bool Success,
    int DeletedFileCount = 0,
    int FailedFileCount = 0,
    string? ErrorMessage = null,
    string? CleanupSkippedReason = null);
