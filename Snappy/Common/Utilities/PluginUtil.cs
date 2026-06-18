using System.Security.Cryptography;
using Penumbra.GameData.Structs;

namespace Snappy.Common.Utilities;

public static class PluginUtil
{
    /// <summary>
    ///     Computes SHA-1 hash for compatibility with Mare file identification.
    ///     Uses the exact same implementation as Mare's Crypto.GetFileHash() method.
    ///     DO NOT change this implementation as it would break Mare interoperability.
    /// </summary>
    public static string GetFileHash(string filePath)
    {
        using var sha1 = SHA1.Create();
        return BitConverter.ToString(sha1.ComputeHash(File.ReadAllBytes(filePath)))
            .Replace("-", "", StringComparison.Ordinal);
    }

    /// <summary>
    ///     Computes SHA-1 hash for compatibility with Mare file identification.
    ///     Uses the exact same implementation as Mare's Crypto.GetFileHash() method.
    ///     DO NOT change this implementation as it would break Mare interoperability.
    /// </summary>
    public static string GetFileHash(byte[] bytes)
    {
        using var sha1 = SHA1.Create();
        return BitConverter.ToString(sha1.ComputeHash(bytes)).Replace("-", "", StringComparison.Ordinal);
    }

    public static bool IsInGpose()
    {
        return Svc.Objects[ObjectIndex.GPosePlayer.Index] != null;
    }
}
