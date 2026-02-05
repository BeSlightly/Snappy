using System.IO.Compression;

namespace Snappy.Common.Utilities;

public record BoneTransformSanitizer
{
    public Vector3? Rotation;
    public Vector3? Scaling;
    public Vector3? Translation;
}

public record ProfileSanitizer
{
    public Dictionary<string, BoneTransformSanitizer> Bones { get; init; } = new();
}

public static class CustomizePlusUtil
{
    public static string DecompressTemplateBase64(string base64Template)
    {
        try
        {
            var compressedBytes = Convert.FromBase64String(base64Template);

            using var compressedStream = new MemoryStream(compressedBytes);
            using var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var resultStream = new MemoryStream();

            gzipStream.CopyTo(resultStream);
            var decompressedBytes = resultStream.ToArray();

            // Skip the version byte (first byte)
            if (decompressedBytes.Length > 1)
            {
                var versionByte = decompressedBytes[0];
                PluginLog.Debug($"Decompressing C+ template version: {versionByte}");
                var jsonBytes = decompressedBytes[1..];
                return Encoding.UTF8.GetString(jsonBytes);
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"Failed to decompress Customize+ template: {ex.Message}");
            return string.Empty;
        }
    }

    public static string CreateCustomizePlusTemplate(string profileJson)
    {
        const byte templateVersionByte = 4;
        try
        {
            var sanitizedProfile = JsonConvert.DeserializeObject<ProfileSanitizer>(profileJson);
            if (sanitizedProfile?.Bones == null)
            {
                PluginLog.Warning($"Could not deserialize C+ profile or it has no bones. JSON: {profileJson}");
                return string.Empty;
            }

            var finalBones = new Dictionary<string, object>();
            foreach (var (key, value) in sanitizedProfile.Bones)
                finalBones[key] = new
                {
                    Translation = value.Translation ?? Vector3.Zero,
                    Rotation = value.Rotation ?? Vector3.Zero,
                    Scaling = value.Scaling ?? Vector3.One
                };

            var finalTemplate = new
            {
                Version = templateVersionByte,
                Bones = finalBones,
                IsWriteProtected = false
            };
            var templateJson = JsonConvert.SerializeObject(finalTemplate, Formatting.None);
            var templateBytes = Encoding.UTF8.GetBytes(templateJson);

            using var compressedStream = new MemoryStream();
            using (var zipStream = new GZipStream(compressedStream, CompressionMode.Compress))
            {
                zipStream.WriteByte(templateVersionByte);
                zipStream.Write(templateBytes, 0, templateBytes.Length);
            }

            return Convert.ToBase64String(compressedStream.ToArray());
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to create Customize+ template.\n{ex}");
            return string.Empty;
        }
    }
}
