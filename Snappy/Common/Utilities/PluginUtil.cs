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
#pragma warning disable SYSLIB0021 // Type or member is obsolete
        using SHA1CryptoServiceProvider cryptoProvider = new();
        return BitConverter.ToString(cryptoProvider.ComputeHash(File.ReadAllBytes(filePath)))
            .Replace("-", "", StringComparison.Ordinal);
#pragma warning restore SYSLIB0021 // Type or member is obsolete
    }

    /// <summary>
    ///     Computes SHA-1 hash for compatibility with Mare file identification.
    ///     Uses the exact same implementation as Mare's Crypto.GetFileHash() method.
    ///     DO NOT change this implementation as it would break Mare interoperability.
    /// </summary>
    public static string GetFileHash(byte[] bytes)
    {
#pragma warning disable SYSLIB0021 // Type or member is obsolete
        using SHA1CryptoServiceProvider cryptoProvider = new();
        return BitConverter.ToString(cryptoProvider.ComputeHash(bytes)).Replace("-", "", StringComparison.Ordinal);
#pragma warning restore SYSLIB0021 // Type or member is obsolete
    }

    public static bool IsInGpose()
    {
        return Svc.Objects[ObjectIndex.GPosePlayer.Index] != null;
    }
}