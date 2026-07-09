using System.IO.Compression;
using System.Threading;
using Snappy.Common;
using Snappy.Features.Pmp.Models;
using Snappy.Features.Packaging;

namespace Snappy.Features.Pmp;

public class PmpExportManager : IPmpExportManager
{
    private readonly Configuration _configuration;
    private int _isExporting;

    public PmpExportManager(Configuration configuration)
    {
        _configuration = configuration;
    }

    public bool IsExporting => Volatile.Read(ref _isExporting) != 0;

    public Task SnapshotToPMPAsync(string snapshotPath, string? outputPath = null, string? fileMapId = null)
        => SnapshotToPMPAsync(snapshotPath, outputPath, fileMapId, null, null, null, false);

    public async Task SnapshotToPMPAsync(string snapshotPath, string? outputPath, string? fileMapId,
        IReadOnlyDictionary<string, string>? fileMapOverride,
        IReadOnlyDictionary<string, string>? fileSwapOverride, string? manipulationOverride,
        bool useReadableArchivePaths = false)
    {
        if (Interlocked.CompareExchange(ref _isExporting, 1, 0) != 0)
        {
            Notify.Warning("An export is already in progress.");
            return;
        }

        string? temporaryOutput = null;
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

            temporaryOutput = AtomicFileUtil.CreateTemporaryOutputPath(pmpOutputPath);

            var resolvedFileMap = fileMapOverride
                                  ?? FileMapUtil.ResolveFileMapWithEmptyFallback(snapshotInfo,
                                      fileMapId ?? snapshotInfo.CurrentFileMapId);

            var resolvedManipulations = manipulationOverride ??
                                        FileMapUtil.ResolveManipulation(snapshotInfo,
                                            fileMapId ?? snapshotInfo.CurrentFileMapId);
            var resolvedFileSwaps = fileSwapOverride
                                    ?? FileMapUtil.ResolveFileSwaps(snapshotInfo,
                                        fileMapId ?? snapshotInfo.CurrentFileMapId);
            var effectiveFileMap = resolvedFileMap
                .Where(kvp => !resolvedFileSwaps.ContainsKey(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);

            using (var fileStream = new FileStream(temporaryOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
            {
                var metadata = ModMetadataBuilder.BuildSnapshotMetadata(snapshotName);
                ArchiveUtil.WriteJsonEntry(archive, "meta.json", metadata);

                var modData = new PmpDefaultMod
                {
                    Manipulations = ModPackageBuilder.BuildManipulations(resolvedManipulations),
                    FileSwaps = new Dictionary<string, string>(resolvedFileSwaps, StringComparer.OrdinalIgnoreCase)
                };
                ModPackageBuilder.AddSnapshotFiles(archive, snapshotInfo, paths.FilesDirectory, modData.Files,
                    effectiveFileMap, useReadableArchivePaths);

                ArchiveUtil.WriteJsonEntry(archive, "default_mod.json", modData);
            }

            AtomicFileUtil.Complete(temporaryOutput, pmpOutputPath);
            temporaryOutput = null;
            Notify.Success($"Successfully exported {snapshotName} to {pmpOutputPath}");
        }
        catch (Exception e)
        {
            Notify.Error($"PMP export failed: {e.Message}");
            PluginLog.Error($"PMP export failed: {e}");
        }
        finally
        {
            if (temporaryOutput != null)
                AtomicFileUtil.TryDelete(temporaryOutput);
            Volatile.Write(ref _isExporting, 0);
        }
    }


}
