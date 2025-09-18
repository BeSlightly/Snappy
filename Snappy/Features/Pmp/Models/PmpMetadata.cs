namespace Snappy.Features.Pmp.Models;

internal record PmpMetadata
{
    public int FileVersion { get; set; } = 3;
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "Snapped!";
    public string Version { get; set; } = "1.0.0";
    public string Website { get; set; } = "";
    public string[] ModTags { get; set; } = [];
}