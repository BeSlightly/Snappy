namespace Snappy.Features.Packaging;

public static class ModMetadataBuilder
{
    public static ModMetadata BuildSnapshotMetadata(string snapshotName, string? sourceActor)
    {
        return new ModMetadata
        {
            Name = snapshotName,
            Author = "Snappy Export",
            Description = $"Exported from Snappy on {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
        };
    }
}

