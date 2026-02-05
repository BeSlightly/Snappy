using System.IO.Compression;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Snappy.Common.Utilities;

public static class GlamourerDesignUtil
{
    public static bool TryEncodeDesignJson(JObject design, out string base64)
    {
        base64 = string.Empty;
        if (design == null)
            return false;

        try
        {
            var designJson = JsonConvert.SerializeObject(design, Formatting.None);
            if (string.IsNullOrWhiteSpace(designJson))
                return false;

            base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(designJson));
            return !string.IsNullOrWhiteSpace(base64);
        }
        catch
        {
            return false;
        }
    }

    public static bool TryDecodeDesignJson(string base64, out JObject? design)
    {
        design = null;
        if (string.IsNullOrWhiteSpace(base64))
            return false;

        try
        {
            var dataBytes = Convert.FromBase64String(base64);
            var gzipStartIndex = -1;
            if (dataBytes.Length > 2 && dataBytes[0] == 0x1F && dataBytes[1] == 0x8B)
                gzipStartIndex = 0;
            else if (dataBytes.Length > 3 && dataBytes[1] == 0x1F && dataBytes[2] == 0x8B)
                gzipStartIndex = 1;

            string designJson;
            if (gzipStartIndex != -1)
            {
                using var compressedStream = new MemoryStream(dataBytes, gzipStartIndex, dataBytes.Length - gzipStartIndex);
                using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
                using var reader = new StreamReader(gzipStream, Encoding.UTF8);
                designJson = reader.ReadToEnd();
            }
            else
            {
                designJson = Encoding.UTF8.GetString(dataBytes);
            }

            if (string.IsNullOrWhiteSpace(designJson))
                return false;

            design = JObject.Parse(designJson);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
