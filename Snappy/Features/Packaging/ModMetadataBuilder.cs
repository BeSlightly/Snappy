namespace Snappy.Features.Packaging;

public static class ModMetadataBuilder
{
    public static ModMetadata BuildSnapshotMetadata(string snapshotName)
    {
        return new ModMetadata
        {
            Name = snapshotName,
            Author = "Snappy Export",
            Description = $"Exported from Snappy on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
        };
    }
}

