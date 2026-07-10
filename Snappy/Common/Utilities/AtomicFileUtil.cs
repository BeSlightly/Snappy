namespace Snappy.Common.Utilities;

public static class AtomicFileUtil
{
    public static string CreateTemporaryOutputPath(string outputPath)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath)!;
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp");
    }

    public static void Complete(string temporaryPath, string outputPath)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullOutputPath))
        {
            File.Replace(temporaryPath, fullOutputPath, null, true);
            return;
        }

        try
        {
            File.Move(temporaryPath, fullOutputPath);
        }
        catch (IOException) when (File.Exists(fullOutputPath))
        {
            File.Replace(temporaryPath, fullOutputPath, null, true);
        }
    }

    public static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // The original export failure is more useful than cleanup failure.
        }
    }
}
