using System.IO;

namespace Snappy.Models
{
    public class Snapshot
    {
        public readonly DirectoryInfo Dir;
        public string Name => Dir.Name;
        public string FullName => Dir.FullName;

        public Snapshot(string path)
        {
            Dir = new DirectoryInfo(path);
        }

        public override bool Equals(object? obj) =>
            obj is Snapshot other
            && FullName.Equals(other.FullName, System.StringComparison.OrdinalIgnoreCase);

        public override int GetHashCode() =>
            FullName.GetHashCode(System.StringComparison.OrdinalIgnoreCase);
    }
}
