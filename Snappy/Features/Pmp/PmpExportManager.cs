using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using Snappy.Common;
using Snappy.Features.Pmp.Models;
using Snappy.Features.Packaging;
using Snappy.Common.Utilities;

namespace Snappy.Features.Pmp;

public class PmpExportManager : IPmpExportManager
{
    private readonly Configuration _configuration;

    public PmpExportManager(Configuration configuration)
    {
        _configuration = configuration;
    }

    public bool IsExporting { get; private set; }

    public Task SnapshotToPMPAsync(string snapshotPath, string? outputPath = null, string? fileMapId = null)
        => SnapshotToPMPAsync(snapshotPath, outputPath, fileMapId, null, null, false);

    public async Task SnapshotToPMPAsync(string snapshotPath, string? outputPath, string? fileMapId,
        IReadOnlyDictionary<string, string>? fileMapOverride, string? manipulationOverride,
        bool useReadableArchivePaths = false)
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
            var pmpOutputPath = outputPath;
            if (string.IsNullOrWhiteSpace(pmpOutputPath))
                pmpOutputPath = Path.Combine(_configuration.WorkingDirectory, $"{snapshotName}.pmp");
            else
                pmpOutputPath = Path.ChangeExtension(pmpOutputPath, ".pmp");

            var resolvedFileMap = fileMapOverride
                                  ?? FileMapUtil.ResolveFileMapWithEmptyFallback(snapshotInfo,
                                      fileMapId ?? snapshotInfo.CurrentFileMapId);

            var resolvedManipulations = manipulationOverride ??
                                        FileMapUtil.ResolveManipulation(snapshotInfo,
                                            fileMapId ?? snapshotInfo.CurrentFileMapId);

            using var fileStream = new FileStream(pmpOutputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create);

            var metadata = ModMetadataBuilder.BuildSnapshotMetadata(snapshotName);
            ArchiveUtil.WriteJsonEntry(archive, "meta.json", metadata);

            // Create default_mod.json entry
            var modData = new PmpDefaultMod();
            modData.Manipulations = ModPackageBuilder.BuildManipulations(resolvedManipulations);
            ModPackageBuilder.AddSnapshotFiles(archive, snapshotInfo, paths.FilesDirectory, modData.Files,
                resolvedFileMap, useReadableArchivePaths);

            ArchiveUtil.WriteJsonEntry(archive, "default_mod.json", modData);

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
