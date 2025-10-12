using System.IO.Compression;
using Newtonsoft.Json.Linq;
using Snappy.Common.Utilities;
using Snappy.Models;

namespace Snappy.Features.Packaging;

public static class ModPackageBuilder
{
    public static List<JObject> BuildManipulations(string base64Manipulations)
        => PenumbraMetaUtil.ConvertPenumbraMetaToJObjects(base64Manipulations);

    public static void AddSnapshotFiles(
        ZipArchive archive,
        SnapshotInfo snapshotInfo,
        string sourceFilesDirectory,
        Dictionary<string, string> filesDictionary)
        => ModpackExportUtil.AddSnapshotFilesToArchive(archive, snapshotInfo, sourceFilesDirectory, filesDictionary);
}

