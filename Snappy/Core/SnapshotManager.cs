using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.ImGuiMethods;
using ECommons.Logging;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Snappy.Models;
using Snappy.Utils;

namespace Snappy.Core
{
    public class SnapshotManager : IDisposable
    {
        private record ActiveSnapshot(
            int ObjectIndex,
            Guid? CustomizePlusProfileId,
            bool IsOnLocalPlayer
        );

        private record SnapshotData(
            string Glamourer,
            string Customize,
            string Manipulation,
            Dictionary<string, string> FileReplacements,
            Dictionary<string, string> ResolvedPaths
        );

        private readonly List<ActiveSnapshot> _activeSnapshots = new();
        private readonly Plugin Plugin;
        private bool _wasInGpose = false;
        private bool _initialized = false;

        private unsafe delegate void ExitGPoseDelegate(UIModule* uiModule);
        private Hook<ExitGPoseDelegate>? _exitGPoseHook;
        private readonly Dictionary<string, string> _snapshotIndex = new(
            StringComparer.OrdinalIgnoreCase
        );

        public event Action? GPoseEntered;
        public event Action? GPoseExited;

        public bool HasActiveSnapshots => _activeSnapshots.Any();

        public unsafe SnapshotManager(Plugin plugin)
        {
            this.Plugin = plugin;
            Svc.Framework.Update += OnFrameworkUpdate;
        }

        public void RefreshSnapshotIndex()
        {
            _snapshotIndex.Clear();
            PluginLog.Debug("Refreshing snapshot index...");

            var workingDir = Plugin.Configuration.WorkingDirectory;
            if (string.IsNullOrEmpty(workingDir) || !Directory.Exists(workingDir))
            {
                PluginLog.Warning(
                    "Working directory not set or not found. Snapshot index will be empty."
                );
                return;
            }

            try
            {
                var snapshotDirs = Directory.GetDirectories(workingDir);
                foreach (var dir in snapshotDirs)
                {
                    var snapshotJsonPath = Path.Combine(dir, "snapshot.json");
                    if (!File.Exists(snapshotJsonPath))
                        continue;

                    try
                    {
                        var jsonContent = File.ReadAllText(snapshotJsonPath);
                        var jObject = JObject.Parse(jsonContent);
                        var sourceActorToken = jObject["SourceActor"];

                        if (sourceActorToken != null && sourceActorToken.Type == JTokenType.String)
                        {
                            var actorName = sourceActorToken.Value<string>();
                            if (!string.IsNullOrEmpty(actorName))
                            {
                                _snapshotIndex[actorName] = dir;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        PluginLog.Warning(
                            $"Could not read or parse snapshot.json in '{Path.GetFileName(dir)}' during index refresh. Skipping. Error: {ex.Message}"
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                PluginLog.Error($"An error occurred while building snapshot index: {ex.Message}");
            }

            PluginLog.Debug($"Snapshot index refreshed. Found {_snapshotIndex.Count} entries.");
        }

        private void OnFrameworkUpdate(IFramework framework)
        {
            // Defer initialization until the client state is ready.
            if (!_initialized)
            {
                Initialize();
            }

            bool isInGpose = Svc.Objects[201] != null;
            if (isInGpose && !_wasInGpose)
            {
                PluginLog.Debug("GPose entered.");
                GPoseEntered?.Invoke();
            }
            _wasInGpose = isInGpose;
        }

        private unsafe void Initialize()
        {
            if (_initialized)
                return;

            try
            {
                var uiModule = Framework.Instance()->UIModule;
                var exitGPoseAddress = (IntPtr)uiModule->VirtualTable->ExitGPose;
                _exitGPoseHook = Svc.Hook.HookFromAddress<ExitGPoseDelegate>(
                    exitGPoseAddress,
                    ExitGPoseDetour
                );
                _exitGPoseHook.Enable();
                _initialized = true;
                PluginLog.Debug("SnapshotManager initialized with ExitGPose hook.");
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Failed to initialize SnapshotManager hook: {ex}");
            }
        }

        private unsafe void ExitGPoseDetour(UIModule* uiModule)
        {
            PluginLog.Debug("GPose exit detected via hook. Running automatic revert logic.");
            GPoseExited?.Invoke();
            var revertedCount = RevertInternal(respectConfig: true);
            if (revertedCount > 0)
            {
                Notify.Info($"Automatically reverted {revertedCount} snapshot(s) on GPose exit.");
            }
            _exitGPoseHook!.Original(uiModule);
        }

        public void Dispose()
        {
            Svc.Framework.Update -= OnFrameworkUpdate;
            RevertAllSnapshots();
            _exitGPoseHook?.Dispose();
        }

        public void RevertAllSnapshots()
        {
            PluginLog.Debug(
                "Manual 'Revert All' triggered. Reverting all snapshots regardless of config."
            );
            var revertedCount = RevertInternal(respectConfig: false);
            if (revertedCount > 0)
            {
                Notify.Success($"Reverted {revertedCount} active snapshot(s).");
            }
            else
            {
                Notify.Info("No active snapshots to revert.");
            }
        }

        private int RevertInternal(bool respectConfig)
        {
            if (!_activeSnapshots.Any())
                return 0;

            var snapshotsToKeep = new List<ActiveSnapshot>();
            var snapshotsToRevert = new List<ActiveSnapshot>();

            foreach (var snapshot in _activeSnapshots)
            {
                if (
                    respectConfig
                    && Plugin.Configuration.DisableAutomaticRevert
                    && snapshot.IsOnLocalPlayer
                )
                {
                    snapshotsToKeep.Add(snapshot);
                }
                else
                {
                    snapshotsToRevert.Add(snapshot);
                }
            }

            if (!snapshotsToRevert.Any())
                return 0;

            PluginLog.Information(
                $"Reverting {snapshotsToRevert.Count} snapshots. Keeping {snapshotsToKeep.Count} snapshots active."
            );

            var indicesToRedraw = new HashSet<int>();

            foreach (var snapshot in snapshotsToRevert)
            {
                // The crucial part: we are now running this logic while the actors still exist.
                var target = Svc.Objects[snapshot.ObjectIndex];

                if (target == null && snapshot.IsOnLocalPlayer)
                {
                    PluginLog.Information(
                        $"Stale snapshot for local player (original index {snapshot.ObjectIndex}) detected. Retargeting to current player character."
                    );
                    target = Player.Object;
                }

                if (target != null)
                {
                    PluginLog.Information(
                        $"Reverting state for actor '{target.Name}' at index {target.ObjectIndex} (original index: {snapshot.ObjectIndex})."
                    );

                    Plugin.IpcManager.PenumbraRemoveTemporaryCollection(snapshot.ObjectIndex);

                    if (snapshot.CustomizePlusProfileId.HasValue)
                    {
                        Plugin.IpcManager.RevertCustomizePlusScale(
                            snapshot.CustomizePlusProfileId.Value
                        );
                    }

                    Plugin.IpcManager.UnlockGlamourerState(target);
                    Plugin.IpcManager.RevertGlamourerToAutomation(target);

                    indicesToRedraw.Add(target.ObjectIndex);
                }
                else
                {
                    PluginLog.Warning(
                        $"Could not find a live actor at index {snapshot.ObjectIndex} to revert, and it was not a player snapshot. Attempting to clear Penumbra collection regardless."
                    );
                    Plugin.IpcManager.PenumbraRemoveTemporaryCollection(snapshot.ObjectIndex);
                }
            }

            _activeSnapshots.Clear();
            _activeSnapshots.AddRange(snapshotsToKeep);

            // Redrawing after the fact is fine, as the game will handle the redraw on the correct (new) actor if necessary.
            foreach (var index in indicesToRedraw)
            {
                if (Svc.Objects[index] != null)
                {
                    PluginLog.Debug($"Requesting redraw for reverted actor at index {index}.");
                    Plugin.IpcManager.PenumbraRedraw(index);
                }
            }

            return snapshotsToRevert.Count;
        }

        public string? FindSnapshotPathForActor(ICharacter character)
        {
            if (character == null)
                return null;
            _snapshotIndex.TryGetValue(character.Name.TextValue, out var path);
            return path;
        }

        private SnapshotData? BuildSnapshotFromLocalPlayer(ICharacter character)
        {
            PluginLog.Debug($"Building snapshot for local player: {character.Name.TextValue}");
            var newGlamourer = Plugin.IpcManager.GetGlamourerState(character);
            var newCustomize = Plugin.IpcManager.GetCustomizePlusScale(character);
            var newManipulation = Plugin.IpcManager.GetMetaManipulations(character.ObjectIndex);
            var newFileReplacements = new Dictionary<string, string>();
            var resolvedPaths = new Dictionary<string, string>();

            var penumbraReplacements = Plugin.IpcManager.PenumbraGetGameObjectResourcePaths(
                character.ObjectIndex
            );
            foreach (var (resolvedPath, gamePaths) in penumbraReplacements)
            {
                if (!File.Exists(resolvedPath))
                    continue;

                var fileBytes = File.ReadAllBytes(resolvedPath);
                var hash = Crypto.GetHash(fileBytes);
                resolvedPaths[hash] = resolvedPath;
                foreach (var gamePath in gamePaths)
                {
                    newFileReplacements[gamePath] = hash;
                }
            }
            return new SnapshotData(
                newGlamourer,
                newCustomize,
                newManipulation,
                newFileReplacements,
                resolvedPaths
            );
        }

        private SnapshotData? BuildSnapshotFromMareData(ICharacter character)
        {
            PluginLog.Debug(
                $"Building snapshot for other player from Mare data: {character.Name.TextValue}"
            );
            var mareCharaData = Plugin.IpcManager.GetCharacterDataFromMare(character);
            if (mareCharaData == null)
            {
                Notify.Error($"Could not get Mare data for {character.Name.TextValue}.");
                return null;
            }

            var newManipulation =
                mareCharaData.GetFoP("ManipulationData") as string ?? string.Empty;

            var glamourerDict = mareCharaData.GetFoP("GlamourerData") as IDictionary;
            var newGlamourer =
                (
                    glamourerDict?.Count > 0
                    && glamourerDict.Keys.Cast<object>().FirstOrDefault(k => (int)k == 0)
                        is { } glamourerKey
                )
                    ? glamourerDict[glamourerKey] as string ?? string.Empty
                    : string.Empty;

            var customizeDict = mareCharaData.GetFoP("CustomizePlusData") as IDictionary;
            var remoteB64Customize =
                (
                    customizeDict?.Count > 0
                    && customizeDict.Keys.Cast<object>().FirstOrDefault(k => (int)k == 0)
                        is { } customizeKey
                )
                    ? customizeDict[customizeKey] as string ?? string.Empty
                    : string.Empty;

            var newCustomize = string.IsNullOrEmpty(remoteB64Customize)
                ? string.Empty
                : Encoding.UTF8.GetString(Convert.FromBase64String(remoteB64Customize));

            var newFileReplacements = new Dictionary<string, string>();
            var fileReplacementsDict = mareCharaData.GetFoP("FileReplacements") as IDictionary;
            if (
                fileReplacementsDict != null
                && fileReplacementsDict.Keys.Cast<object>().FirstOrDefault(k => (int)k == 0)
                    is { } playerKey
            )
            {
                var fileList = fileReplacementsDict[playerKey] as IEnumerable;
                if (fileList != null)
                {
                    foreach (var fileData in fileList)
                    {
                        var gamePaths = fileData.GetFoP("GamePaths") as string[];
                        var hash = fileData.GetFoP("Hash") as string;
                        if (gamePaths != null && !string.IsNullOrEmpty(hash))
                        {
                            foreach (var path in gamePaths)
                            {
                                newFileReplacements[path] = hash;
                            }
                        }
                    }
                }
            }

            return new SnapshotData(
                newGlamourer,
                newCustomize,
                newManipulation,
                newFileReplacements,
                new Dictionary<string, string>()
            ); // ResolvedPaths is empty for Mare data
        }

        public string? UpdateSnapshot(ICharacter character)
        {
            bool isSelf =
                Player.Object?.Address == character.Address || character.ObjectIndex == 201;

            SnapshotData? snapshotData = isSelf
                ? BuildSnapshotFromLocalPlayer(character)
                : BuildSnapshotFromMareData(character);

            if (snapshotData == null)
            {
                return null;
            }

            var charaName = character.Name.TextValue;
            var snapshotPath =
                FindSnapshotPathForActor(character)
                ?? Path.Combine(Plugin.Configuration.WorkingDirectory, charaName);

            var filesPath = Path.Combine(snapshotPath, "_files");
            Directory.CreateDirectory(snapshotPath);
            Directory.CreateDirectory(filesPath);

            var snapshotInfoPath = Path.Combine(snapshotPath, "snapshot.json");
            var glamourerHistoryPath = Path.Combine(snapshotPath, "glamourer_history.json");
            var customizeHistoryPath = Path.Combine(snapshotPath, "customize_history.json");

            bool isNewSnapshot = !File.Exists(snapshotInfoPath);

            SnapshotInfo snapshotInfo = isNewSnapshot
                ? new SnapshotInfo { SourceActor = charaName }
                : JsonConvert.DeserializeObject<SnapshotInfo>(File.ReadAllText(snapshotInfoPath))!;

            GlamourerHistory glamourerHistory = File.Exists(glamourerHistoryPath)
                ? JsonConvert.DeserializeObject<GlamourerHistory>(
                    File.ReadAllText(glamourerHistoryPath)
                )!
                : new GlamourerHistory();

            CustomizeHistory customizeHistory = File.Exists(customizeHistoryPath)
                ? JsonConvert.DeserializeObject<CustomizeHistory>(
                    File.ReadAllText(customizeHistoryPath)
                )!
                : new CustomizeHistory();

            foreach (var (gamePath, hash) in snapshotData.FileReplacements)
            {
                snapshotInfo.FileReplacements[gamePath] = hash;

                var hashedFilePath = Path.Combine(filesPath, hash + ".dat");
                if (!File.Exists(hashedFilePath))
                {
                    var sourceFile = isSelf
                        ? snapshotData.ResolvedPaths[hash]
                        : Plugin.IpcManager.GetMareFileCachePath(hash);

                    if (!string.IsNullOrEmpty(sourceFile) && File.Exists(sourceFile))
                    {
                        File.Copy(sourceFile, hashedFilePath, true);
                    }
                    else
                    {
                        PluginLog.Warning(
                            $"Could not find source file for {gamePath} (hash: {hash})."
                        );
                    }
                }
            }

            snapshotInfo.ManipulationString = snapshotData.Manipulation;

            var lastGlamourerEntry = glamourerHistory.Entries.LastOrDefault();
            if (
                lastGlamourerEntry == null
                || lastGlamourerEntry.GlamourerString != snapshotData.Glamourer
            )
            {
                var now = DateTime.UtcNow;
                var newEntry = GlamourerHistoryEntry.Create(
                    snapshotData.Glamourer,
                    $"Glamourer Update - {now:yyyy-MM-dd HH:mm:ss} UTC"
                );
                glamourerHistory.Entries.Add(newEntry);
                PluginLog.Debug("New Glamourer version detected. Appending to history.");
            }

            var b64Customize = string.IsNullOrEmpty(snapshotData.Customize)
                ? ""
                : Convert.ToBase64String(Encoding.UTF8.GetBytes(snapshotData.Customize));
            var lastCustomizeEntry = customizeHistory.Entries.LastOrDefault();
            if (
                (lastCustomizeEntry == null || lastCustomizeEntry.CustomizeData != b64Customize)
                && !string.IsNullOrEmpty(b64Customize)
            )
            {
                var now = DateTime.UtcNow;
                var newEntry = CustomizeHistoryEntry.CreateFromBase64(
                    b64Customize,
                    snapshotData.Customize,
                    $"Customize+ Update - {now:yyyy-MM-dd HH:mm:ss} UTC"
                );
                customizeHistory.Entries.Add(newEntry);
                PluginLog.Debug("New Customize+ version detected. Appending to history.");
            }

            snapshotInfo.LastUpdate = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

            SaveSnapshotToDisk(snapshotPath, snapshotInfo, glamourerHistory, customizeHistory);

            if (isNewSnapshot)
            {
                Notify.Success($"New snapshot for '{charaName}' created successfully.");
            }
            else
            {
                Notify.Success($"Snapshot for '{charaName}' updated successfully.");
            }

            return snapshotPath;
        }

        public bool LoadSnapshot(
            ICharacter characterApplyTo,
            int objIdx,
            string path,
            GlamourerHistoryEntry? glamourerOverride = null,
            CustomizeHistoryEntry? customizeOverride = null
        )
        {
            var snapshotInfoPath = Path.Combine(path, "snapshot.json");
            var glamourerHistoryPath = Path.Combine(path, "glamourer_history.json");
            var customizeHistoryPath = Path.Combine(path, "customize_history.json");
            var filesPath = Path.Combine(path, "_files");

            if (!File.Exists(snapshotInfoPath))
            {
                Notify.Error($"Could not load snapshot: snapshot.json not found in {path}");
                return false;
            }

            SnapshotInfo snapshotInfo = JsonConvert.DeserializeObject<SnapshotInfo>(
                File.ReadAllText(snapshotInfoPath)
            )!;
            GlamourerHistory glamourerHistory = File.Exists(glamourerHistoryPath)
                ? JsonConvert.DeserializeObject<GlamourerHistory>(
                    File.ReadAllText(glamourerHistoryPath)
                )!
                : new();
            CustomizeHistory customizeHistory = File.Exists(customizeHistoryPath)
                ? JsonConvert.DeserializeObject<CustomizeHistory>(
                    File.ReadAllText(customizeHistoryPath)
                )!
                : new();

            var glamourerToApply = glamourerOverride ?? glamourerHistory.Entries.LastOrDefault();
            var customizeToApply = customizeOverride ?? customizeHistory.Entries.LastOrDefault();

            if (
                glamourerToApply == null
                && customizeToApply == null
                && !snapshotInfo.FileReplacements.Any()
            )
            {
                Notify.Error("Could not load snapshot: No data (files, glamour, C+) to apply.");
                return false;
            }

            Dictionary<string, string> moddedPaths = new();
            if (snapshotInfo.FileReplacements.Any())
            {
                foreach (var replacement in snapshotInfo.FileReplacements)
                {
                    var gamePath = replacement.Key;
                    var hash = replacement.Value;
                    var hashedFilePath = Path.Combine(filesPath, hash + ".dat");

                    if (File.Exists(hashedFilePath))
                    {
                        moddedPaths[gamePath] = hashedFilePath;
                    }
                    else
                    {
                        PluginLog.Warning(
                            $"Missing file blob for {gamePath} (hash: {hash}). It will not be applied."
                        );
                    }
                }
                Plugin.IpcManager.PenumbraRemoveTemporaryCollection(characterApplyTo.ObjectIndex);
                Plugin.IpcManager.PenumbraSetTempMods(
                    characterApplyTo,
                    objIdx,
                    moddedPaths,
                    snapshotInfo.ManipulationString
                );
            }

            _activeSnapshots.RemoveAll(s => s.ObjectIndex == characterApplyTo.ObjectIndex);
            Guid? cplusProfileId = null;

            if (
                Plugin.IpcManager.IsCustomizePlusAvailable()
                && customizeToApply != null
                && !string.IsNullOrEmpty(customizeToApply.CustomizeData)
            )
            {
                string cplusJson;
                try
                {
                    cplusJson = Encoding.UTF8.GetString(
                        Convert.FromBase64String(customizeToApply.CustomizeData)
                    );
                }
                catch
                {
                    cplusJson = customizeToApply.CustomizeData;
                }
                cplusProfileId = Plugin.IpcManager.SetCustomizePlusScale(
                    characterApplyTo.Address,
                    cplusJson
                );
            }

            if (glamourerToApply != null)
            {
                Plugin.IpcManager.ApplyGlamourerState(
                    glamourerToApply.GlamourerString,
                    characterApplyTo
                );
            }

            Plugin.IpcManager.PenumbraRedraw(objIdx);

            bool isOnLocalPlayer =
                (Player.Available && characterApplyTo.ObjectIndex == Player.Object.ObjectIndex)
                || characterApplyTo.ObjectIndex == 201;

            _activeSnapshots.Add(
                new ActiveSnapshot(characterApplyTo.ObjectIndex, cplusProfileId, isOnLocalPlayer)
            );
            PluginLog.Debug(
                $"Snapshot loaded for index {characterApplyTo.ObjectIndex}. 'IsOnLocalPlayer' flag set to: {isOnLocalPlayer}."
            );

            var snapshotName = Path.GetFileName(path);
            Notify.Success(
                $"Loaded snapshot '{snapshotName}' onto {characterApplyTo.Name.TextValue}."
            );

            return true;
        }

        public void SaveSnapshotToDisk(
            string snapshotPath,
            SnapshotInfo info,
            GlamourerHistory glamourerHistory,
            CustomizeHistory customizeHistory
        )
        {
            var snapshotInfoPath = Path.Combine(snapshotPath, "snapshot.json");
            var glamourerHistoryPath = Path.Combine(snapshotPath, "glamourer_history.json");
            var customizeHistoryPath = Path.Combine(snapshotPath, "customize_history.json");

            File.WriteAllText(
                snapshotInfoPath,
                JsonConvert.SerializeObject(info, Formatting.Indented)
            );
            File.WriteAllText(
                glamourerHistoryPath,
                JsonConvert.SerializeObject(glamourerHistory, Formatting.Indented)
            );
            File.WriteAllText(
                customizeHistoryPath,
                JsonConvert.SerializeObject(customizeHistory, Formatting.Indented)
            );
        }
    }
}
