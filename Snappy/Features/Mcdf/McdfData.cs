using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace Snappy.Features.Mcdf;

public record McdfData
{
    public string Description { get; set; } = string.Empty;
    public string GlamourerData { get; set; } = string.Empty;
    public string CustomizePlusData { get; set; } = string.Empty;
    public string ManipulationData { get; set; } = string.Empty;
    public List<FileData> Files { get; set; } = new();

    public McdfData() { }

    public byte[] ToByteArray()
    {
        return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(this));
    }

    public static McdfData FromByteArray(byte[] data)
    {
        return JsonConvert.DeserializeObject<McdfData>(Encoding.UTF8.GetString(data))!;
    }

    public record FileData(IEnumerable<string> GamePaths, long Length);
}
