using System.IO.Compression;
using Newtonsoft.Json.Linq;

namespace Snappy.Common.Utilities;

public static class CustomizePlusUtil
{
    // Customize+ currently writes version 6 templates. Older versions remain readable by C+.
    private const byte TemplateVersion = 6;

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
            if (decompressedBytes.Length <= 1)
                return string.Empty;

            PluginLog.Debug($"Decompressing C+ template version: {decompressedBytes[0]}");
            return Encoding.UTF8.GetString(decompressedBytes, 1, decompressedBytes.Length - 1);
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"Failed to decompress Customize+ template: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>
    /// Convert either an IPC profile or a PCP template to the profile shape accepted by
    /// Customize+'s SetTemporaryProfileOnCharacter IPC.
    /// </summary>
    public static bool TryNormalizeIpcProfileJson(string profileJson, out string normalizedProfileJson)
    {
        normalizedProfileJson = string.Empty;
        if (!TryGetBones(profileJson, out var bones))
            return false;

        var normalized = new JObject
        {
            ["Bones"] = CreateIpcBones(bones)
        };
        normalizedProfileJson = normalized.ToString(Formatting.None);
        return true;
    }

    /// <summary>
    /// Create a current Customize+ template from either an IPC profile or a template payload.
    /// </summary>
    public static bool TryCreateTemplateJson(string profileJson, out string templateJson, string? templateName = null)
    {
        templateJson = string.Empty;
        if (!TryGetBones(profileJson, out var bones, out var source))
            return false;

        var now = DateTimeOffset.UtcNow;
        var sourceName = source["Name"]?.Value<string>();
        var name = string.IsNullOrWhiteSpace(templateName)
            ? string.IsNullOrWhiteSpace(sourceName) ? "Snappy Template" : sourceName
            : templateName;
        var template = new JObject
        {
            ["Version"] = TemplateVersion,
            ["UniqueId"] = Guid.NewGuid(),
            ["CreationDate"] = now,
            ["ModifiedDate"] = now,
            ["Name"] = name,
            ["Bones"] = CreateTemplateBones(bones),
            ["IsWriteProtected"] = false
        };

        templateJson = template.ToString(Formatting.None);
        return true;
    }

    public static string CreateCustomizePlusTemplate(string profileJson)
    {
        try
        {
            if (!TryCreateTemplateJson(profileJson, out var templateJson))
            {
                PluginLog.Warning("Could not deserialize Customize+ profile or it has no bones.");
                return string.Empty;
            }

            var templateBytes = Encoding.UTF8.GetBytes(templateJson);
            using var compressedStream = new MemoryStream();
            using (var zipStream = new GZipStream(compressedStream, CompressionMode.Compress))
            {
                zipStream.WriteByte(TemplateVersion);
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

    private static bool TryGetBones(string json, out JObject bones)
        => TryGetBones(json, out bones, out _);

    private static bool TryGetBones(string json, out JObject bones, out JObject source)
    {
        bones = null!;
        source = null!;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            source = JObject.Parse(json);
            if (source["Bones"] is not JObject parsedBones)
                return false;

            bones = parsedBones;
            return true;
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"Failed to parse Customize+ profile data: {ex.Message}");
            return false;
        }
    }

    private static JObject CreateIpcBones(JObject sourceBones)
    {
        var result = new JObject();
        foreach (var bone in sourceBones.Properties())
        {
            if (bone.Value is not JObject sourceBone)
                continue;

            result[bone.Name] = new JObject
            {
                ["Translation"] = GetValueOrDefault(sourceBone, "Translation", Vector3.Zero),
                ["Rotation"] = GetValueOrDefault(sourceBone, "Rotation", Vector3.Zero),
                ["Scaling"] = GetValueOrDefault(sourceBone, "Scaling", Vector3.One),
                ["ChildScaling"] = GetValueOrDefault(sourceBone, "ChildScaling", Vector3.One),
                ["PropagateTranslation"] = GetValueOrDefault(sourceBone, "PropagateTranslation", false),
                ["PropagateRotation"] = GetValueOrDefault(sourceBone, "PropagateRotation", false),
                ["PropagateScale"] = GetValueOrDefault(sourceBone, "PropagateScale", false),
                ["ChildScaleIndependent"] = GetValueOrDefault(sourceBone, "ChildScaleIndependent",
                    "ChildScalingIndependent", false)
            };
        }

        return result;
    }

    private static JObject CreateTemplateBones(JObject sourceBones)
    {
        var result = new JObject();
        foreach (var bone in sourceBones.Properties())
        {
            if (bone.Value is not JObject sourceBone)
                continue;

            result[bone.Name] = new JObject
            {
                ["Translation"] = GetValueOrDefault(sourceBone, "Translation", Vector3.Zero),
                ["Rotation"] = GetValueOrDefault(sourceBone, "Rotation", Vector3.Zero),
                ["Scaling"] = GetValueOrDefault(sourceBone, "Scaling", Vector3.One),
                ["ChildScaling"] = GetValueOrDefault(sourceBone, "ChildScaling", Vector3.One),
                ["PropagateTranslation"] = GetValueOrDefault(sourceBone, "PropagateTranslation", false),
                ["PropagateRotation"] = GetValueOrDefault(sourceBone, "PropagateRotation", false),
                ["PropagateScale"] = GetValueOrDefault(sourceBone, "PropagateScale", false),
                ["ChildScalingIndependent"] = GetValueOrDefault(sourceBone, "ChildScalingIndependent",
                    "ChildScaleIndependent", false)
            };
        }

        return result;
    }

    private static JToken GetValueOrDefault(JObject source, string propertyName, object defaultValue)
        => source[propertyName]?.DeepClone() ?? JToken.FromObject(defaultValue);

    private static JToken GetValueOrDefault(JObject source, string propertyName, string fallbackPropertyName,
        object defaultValue)
        => source[propertyName]?.DeepClone()
           ?? source[fallbackPropertyName]?.DeepClone()
           ?? JToken.FromObject(defaultValue);
}
