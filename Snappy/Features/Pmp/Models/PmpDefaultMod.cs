using Newtonsoft.Json.Linq;

namespace Snappy.Features.Pmp.Models;

internal record PmpDefaultMod
{
    public int Version { get; set; } = 0;
    public Dictionary<string, string> Files { get; set; } = new();
    public Dictionary<string, string> FileSwaps { get; set; } = new();
    public List<JObject> Manipulations { get; set; } = new();
}
