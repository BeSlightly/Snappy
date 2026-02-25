using Luna;

namespace Snappy.Models;

public sealed record Snapshot(string FullName) : IFileSystemValue<Snapshot>
{
    public string Name => System.IO.Path.GetFileName(FullName);
    public DirectoryInfo Dir => new(FullName);
    public string DisplayName => Name;
    public DataPath Path { get; } = new();
    public string Identifier => FullName;
    public IFileSystemData<Snapshot>? Node { get; set; }
}
