namespace Snappy.Models;

public record Snapshot(string FullName)
{
    public string Name => Path.GetFileName(FullName);
    public DirectoryInfo Dir => new(FullName);
}