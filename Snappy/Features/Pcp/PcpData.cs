using Newtonsoft.Json.Linq;

namespace Snappy.Features.Pcp;

public record PcpMetadata
{
    public int FileVersion { get; set; } = 3;
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Website { get; set; } = string.Empty;
    public List<string> ModTags { get; set; } = ["PCP"];
    public List<object> DefaultPreferredItems { get; set; } = [];
}

public record PcpCharacterData
{
    public int Version { get; set; } = 1;
    public PcpActor Actor { get; set; } = new();
    public string Mod { get; set; } = string.Empty;
    public string Collection { get; set; } = string.Empty;
    public DateTime Time { get; set; } = DateTime.Now;
    public string Note { get; set; } = string.Empty;
    public object? CustomizePlus { get; set; }
    public object? Glamourer { get; set; }
}

public record PcpActor
{
    public string Type { get; set; } = "Player";
    public string PlayerName { get; set; } = string.Empty;
    public int HomeWorld { get; set; } = 0;
}

public record PcpModData
{
    public int Version { get; set; } = 0;
    public Dictionary<string, string> Files { get; set; } = [];
    public Dictionary<string, string> FileSwaps { get; set; } = [];
    public List<JObject> Manipulations { get; set; } = [];
}