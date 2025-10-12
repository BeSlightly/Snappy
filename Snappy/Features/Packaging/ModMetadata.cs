namespace Snappy.Features.Packaging;

public record ModMetadata
{
    public int FileVersion { get; set; } = 3;
    public string Name { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Website { get; set; } = string.Empty;
    public List<string> ModTags { get; set; } = [];
    public List<object> DefaultPreferredItems { get; set; } = [];
}

