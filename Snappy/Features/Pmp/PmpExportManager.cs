using System.IO.Compression;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Snappy.Common;
using Snappy.Features;
using Snappy.Features.Pmp.Models;
using Snappy.Common.Utilities;
using Snappy.Features.Packaging;

namespace Snappy.Features.Pmp;

public class PmpExportManager : IPmpExportManager
{
    private readonly Configuration _configuration;

    public PmpExportManager(Configuration configuration)
    {
        _configuration = configuration;
    }

    public bool IsExporting { get; private set; }

    public async Task SnapshotToPMPAsync(string snapshotPath)
    {
        if (IsExporting)
        {
            Notify.Warning("An export is already in progress.");
            return;
        }

        IsExporting = true;
        try
        {
            PluginLog.Debug($"Operating on {snapshotPath}");

            var paths = SnapshotPaths.From(snapshotPath);
            var snapshotInfo = await JsonUtil.DeserializeAsync<SnapshotInfo>(paths.SnapshotFile);
            if (snapshotInfo == null)
            {
                Notify.Error("Export failed: Could not read snapshot.json.");
                return;
            }

            var snapshotName = new DirectoryInfo(snapshotPath).Name;
            var pmpOutputPath = Path.Combine(_configuration.WorkingDirectory, $"{snapshotName}.pmp");

            using var fileStream = new FileStream(pmpOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

            // Create meta.json entry
            var metaEntry = archive.CreateEntry("meta.json");
            using (var streamWriter = new StreamWriter(metaEntry.Open()))
            {
                var metadata = ModMetadataBuilder.BuildSnapshotMetadata(snapshotName, snapshotInfo.SourceActor);
                await streamWriter.WriteAsync(JsonConvert.SerializeObject(metadata, Formatting.Indented));
            }

            // Create default_mod.json entry
            var modData = new PmpDefaultMod();
            modData.Manipulations = ModPackageBuilder.BuildManipulations(snapshotInfo.ManipulationString);
            ModPackageBuilder.AddSnapshotFiles(archive, snapshotInfo, paths.FilesDirectory, modData.Files);

            var modEntry = archive.CreateEntry("default_mod.json");
            using (var streamWriter = new StreamWriter(modEntry.Open()))
            {
                await streamWriter.WriteAsync(JsonConvert.SerializeObject(modData, Formatting.Indented));
            }

            Notify.Success($"Successfully exported {snapshotName} to {pmpOutputPath}");
        }
        catch (Exception e)
        {
            Notify.Error($"PMP export failed: {e.Message}");
            PluginLog.Error($"PMP export failed: {e}");
        }
        finally
        {
            IsExporting = false;
        }
    }


}
