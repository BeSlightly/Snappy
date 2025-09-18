namespace Snappy.Features.Pmp.Models;

internal record PmpDefaultMod
{
    public string Name { get; } = "Default";
    public string Description { get; } = "";
    public Dictionary<string, string> Files { get; set; } = new();
    public List<PmpManipulationEntry> Manipulations { get; set; } = new();
}