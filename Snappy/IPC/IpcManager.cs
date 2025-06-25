using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using ECommons.DalamudServices;
using ECommons.Logging;
using ECommons.Reflection;
using Snappy.IPC.Customize;
using Snappy.IPC.Glamourer;
using Snappy.IPC.Penumbra;
using Snappy.Utils;

namespace Snappy.IPC;

public class IpcManager : IDisposable
{
    private readonly DalamudUtil _dalamudUtil;

    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly CustomizeIpc _customize;

    public IpcManager(IDalamudPluginInterface pi, DalamudUtil dalamudUtil)
    {
        PluginLog.Debug("Creating IpcManager delegator");

        _dalamudUtil = dalamudUtil;
        _penumbra = new PenumbraIpc();
        _glamourer = new GlamourerIpc();
        _customize = new CustomizeIpc();
    }

    public void Dispose()
    {
        _penumbra.Dispose();
        _glamourer.Dispose();
        _customize.Dispose();
    }

    // Penumbra passthroughs
    public void PenumbraRemoveTemporaryCollection(int objIdx) =>
        _penumbra.RemoveTemporaryCollection(objIdx);

    public void PenumbraRedraw(int objIdx) => _penumbra.Redraw(objIdx);

    public void PenumbraRedraw(IntPtr objPtr) => _penumbra.Redraw(objPtr);

    public string GetMetaManipulations(int objIdx) => _penumbra.GetMetaManipulations(objIdx);

    public Dictionary<string, HashSet<string>> PenumbraGetGameObjectResourcePaths(int objIdx) =>
        _penumbra.GetGameObjectResourcePaths(objIdx);

    public void PenumbraSetTempMods(
        ICharacter character,
        int? idx,
        Dictionary<string, string> mods,
        string manips
    ) => _penumbra.SetTemporaryMods(character, idx, mods, manips);

    public string PenumbraResolvePath(string path) => _penumbra.ResolvePath(path);

    public string PenumbraResolvePathObject(string path, int objIdx) =>
        _penumbra.ResolvePathObject(path, objIdx);

    public string[] PenumbraReverseResolveObject(string path, int objIdx) =>
        _penumbra.ReverseResolveObject(path, objIdx);

    public string[] PenumbraReverseResolvePlayer(string path) =>
        _penumbra.ReverseResolvePlayer(path);

    // Glamourer passthroughs
    public GlamourerIpc GlamourerIpc => _glamourer;

    public string GetGlamourerState(ICharacter c) => _glamourer.GetCharacterCustomization(c);

    public void ApplyGlamourerState(string? base64, ICharacter c) =>
        _glamourer.ApplyState(base64, c);

    public void UnlockGlamourerState(IGameObject c) => _glamourer.UnlockState(c);

    public void RevertGlamourerToAutomation(IGameObject c) => _glamourer.RevertToAutomation(c);

    // CustomizePlus passthroughs
    public bool IsCustomizePlusAvailable() => _customize.CheckApi();

    public string GetCustomizePlusScale(ICharacter c) => _customize.GetScaleFromCharacter(c);

    public Guid? SetCustomizePlusScale(IntPtr address, string scale) =>
        _customize.SetScale(address, scale);

    public void RevertCustomizePlusScale(Guid profileId) => _customize.Revert(profileId);

    // Mare passthroughs
    private List<ICharacter> _cachedMarePairedPlayers = new();
    private DateTime _lastMareCacheUpdateTime = DateTime.MinValue;
    private readonly TimeSpan _mareCacheDuration = TimeSpan.FromSeconds(5);

    private IDictionary? GetAllMareClientPairs()
    {
        if (!DalamudReflector.TryGetDalamudPlugin("MareSynchronos", out var marePlugin, true, true))
            return null;

        try
        {
            var host = marePlugin.GetFoP("_host");
            if (host == null)
                return null;
            var serviceProvider = host.GetFoP("Services") as IServiceProvider;
            if (serviceProvider == null)
                return null;
            var pairManagerType = marePlugin
                .GetType()
                .Assembly.GetType("MareSynchronos.PlayerData.Pairs.PairManager");
            if (pairManagerType == null)
                return null;
            var pairManager = serviceProvider.GetService(pairManagerType);
            if (pairManager == null)
                return null;
            return pairManager.GetFoP("_allClientPairs") as IDictionary;
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while reflecting into Mare Synchronos to get client pairs.\n{e}"
            );
            return null;
        }
    }

    private object? GetMarePair(ICharacter character)
    {
        var allClientPairs = GetAllMareClientPairs();
        if (allClientPairs == null)
            return null;

        try
        {
            foreach (var pairObject in allClientPairs.Values)
            {
                var pairPlayerName = pairObject.GetFoP("PlayerName") as string;
                if (
                    string.Equals(
                        pairPlayerName,
                        character.Name.TextValue,
                        StringComparison.Ordinal
                    )
                )
                {
                    return pairObject;
                }
            }
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while processing Mare Synchronos pairs to find a specific pair.\n{e}"
            );
        }

        return null;
    }

    public List<ICharacter> GetMarePairedPlayers()
    {
        if (DateTime.UtcNow - _lastMareCacheUpdateTime < _mareCacheDuration)
        {
            _cachedMarePairedPlayers.RemoveAll(c => !c.IsValid());
            return _cachedMarePairedPlayers;
        }

        PluginLog.Debug("Refreshing Mare paired players cache.");
        _lastMareCacheUpdateTime = DateTime.UtcNow;

        var result = new List<ICharacter>();
        var allClientPairs = GetAllMareClientPairs();
        if (allClientPairs == null)
        {
            _cachedMarePairedPlayers = result;
            return result;
        }

        var playersInZone = Svc
            .Objects.OfType<IPlayerCharacter>()
            .Where(p => p.IsValid())
            .ToDictionary(p => p.Name.TextValue, p => (ICharacter)p, StringComparer.Ordinal);

        try
        {
            foreach (var pairObject in allClientPairs.Values)
            {
                var isVisible = pairObject.GetFoP("IsVisible") as bool? ?? false;
                var playerName = pairObject.GetFoP("PlayerName") as string;
                if (
                    isVisible
                    && !string.IsNullOrEmpty(playerName)
                    && playersInZone.TryGetValue(playerName, out var character)
                )
                {
                    result.Add(character);
                }
            }
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while processing Mare Synchronos pairs to get visible players.\n{e}"
            );
        }

        _cachedMarePairedPlayers = result;
        return result;
    }

    public object? GetCharacterDataFromMare(ICharacter character)
    {
        return _dalamudUtil
            .RunOnFrameworkThread(() =>
            {
                var pairObject = GetMarePair(character);
                if (pairObject == null)
                {
                    PluginLog.Debug(
                        $"No Mare pair found for character {character.Name.TextValue}."
                    );
                    return null;
                }
                return pairObject.GetFoP("LastReceivedCharacterData");
            })
            .Result;
    }

    public string? GetMareFileCachePath(string hash)
    {
        if (!DalamudReflector.TryGetDalamudPlugin("MareSynchronos", out var marePlugin, true, true))
            return null;

        try
        {
            var host = marePlugin.GetFoP("_host");
            if (host == null)
                return null;
            var serviceProvider = host.GetFoP("Services") as IServiceProvider;
            if (serviceProvider == null)
                return null;
            var fileCacheManagerType = marePlugin
                .GetType()
                .Assembly.GetType("MareSynchronos.FileCache.FileCacheManager");
            if (fileCacheManagerType == null)
            {
                PluginLog.Warning(
                    "Reflection failed: Could not find type MareSynchronos.FileCache.FileCacheManager."
                );
                return null;
            }
            var fileCacheManager = serviceProvider.GetService(fileCacheManagerType);
            if (fileCacheManager == null)
            {
                PluginLog.Warning(
                    "Reflection failed: Could not get FileCacheManager service from IServiceProvider."
                );
                return null;
            }

            var getFileCacheMethod = fileCacheManagerType.GetMethod(
                "GetFileCacheByHash",
                new[] { typeof(string) }
            );
            if (getFileCacheMethod == null)
            {
                PluginLog.Warning(
                    "Reflection failed: Could not find method GetFileCacheByHash in FileCacheManager."
                );
                return null;
            }

            var fileCacheEntityObject = getFileCacheMethod.Invoke(
                fileCacheManager,
                new object[] { hash }
            );
            if (fileCacheEntityObject == null)
                return null;

            return fileCacheEntityObject.GetFoP("ResolvedFilepath") as string;
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while reflecting into Mare Synchronos for file cache path.\n{e}"
            );
            return null;
        }
    }
}
