using System.Security.Cryptography;
using Penumbra.GameData.Structs;

namespace Snappy.Common.Utilities;

public static class PluginUtil
{
    /// <summary>Computes the SHA-1 identifier expected by Mare.</summary>
    public static string GetFileHash(string filePath)
    {
        using var sha1 = SHA1.Create();
        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(sha1.ComputeHash(stream));
    }

    /// <summary>Computes the SHA-1 identifier expected by Mare.</summary>
    public static string GetFileHash(byte[] bytes)
    {
        using var sha1 = SHA1.Create();
        return Convert.ToHexString(sha1.ComputeHash(bytes));
    }

    public static bool IsInGpose()
    {
        return Svc.Objects[ObjectIndex.GPosePlayer.Index] != null;
    }
}
