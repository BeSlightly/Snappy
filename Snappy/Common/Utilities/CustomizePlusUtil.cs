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