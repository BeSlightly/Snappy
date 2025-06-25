using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using ECommons.Logging;
using Newtonsoft.Json;

namespace Snappy.Utils
{
    public record BoneTransformSanitizer
    {
        public Vector3? Translation;
        public Vector3? Rotation;
        public Vector3? Scaling;
    }

    public record ProfileSanitizer
    {
        public Dictionary<string, BoneTransformSanitizer> Bones = new();
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
                    PluginLog.Warning(
                        $"Could not deserialize C+ profile or it has no bones. JSON: {profileJson}"
                    );
                    return string.Empty;
                }

                var finalBones = new Dictionary<string, object>();
                foreach (var bone in sanitizedProfile.Bones)
                {
                    finalBones[bone.Key] = new
                    {
                        Translation = bone.Value.Translation ?? Vector3.Zero,
                        Rotation = bone.Value.Rotation ?? Vector3.Zero,
                        Scaling = bone.Value.Scaling ?? Vector3.One,
                    };
                }

                var finalTemplate = new
                {
                    Version = templateVersionByte,
                    Bones = finalBones,
                    IsWriteProtected = false,
                };
                var templateJson = JsonConvert.SerializeObject(
                    finalTemplate,
                    Newtonsoft.Json.Formatting.None
                );
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
}
