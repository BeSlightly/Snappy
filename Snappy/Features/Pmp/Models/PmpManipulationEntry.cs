namespace Snappy.Features.Pmp.Models;

internal record PmpManipulationEntry
{
    public string? Type { get; set; } = string.Empty;
    public object? Manipulation { get; set; }
}