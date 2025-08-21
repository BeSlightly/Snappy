using System.Collections;
using System.Reflection;
using Dalamud.Plugin;
using ECommons.EzIpcManager;
using ECommons.Reflection;
using Snappy.Services;

namespace Snappy.Integrations;

public sealed class MareIpc : IpcSubscriber
{
    private const string PluginName = "MareSynchronos";
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(1);
    private readonly IDalamudPluginInterface _pluginInterface;

    private List<ICharacter> _cachedPairedPlayers = new();
    private object? _fileCacheManager;
    private MethodInfo? _getFileCacheByHashMethod;
    private bool _isInitialized;
    private bool _isUiOpen;
    private DateTime _lastCacheUpdate = DateTime.MinValue;
    private object? _pairManager;

    public MareIpc() : base(PluginName)
    {
        _pluginInterface = Svc.PluginInterface;
    }

    [EzIPC("MareSynchronos.GetHandledAddresses", false, wrapper: SafeWrapper.AnyException)]
    public Func<List<nint>>? GetHandledAddressesIpc { get; set; }

    public void SetUiOpen(bool isOpen)
    {
        if (_isUiOpen == isOpen) return;
        _isUiOpen = isOpen;

        if (!isOpen)
        {
            _cachedPairedPlayers.Clear();
            _lastCacheUpdate = DateTime.MinValue;
            PluginLog.Debug("UI closed - cleared Mare paired players cache.");
        } else
        {
            PluginLog.Debug("UI opened - Mare paired players cache will be refreshed on next request.");
        }
    }

    public List<ICharacter> GetPairedPlayers()
    {
        if (!_isUiOpen || !IsReady()) return new List<ICharacter>();

        if (DateTime.UtcNow - _lastCacheUpdate < _cacheDuration)
        {
            _cachedPairedPlayers.RemoveAll(c => !c.IsValid());
            return _cachedPairedPlayers;
        }

        return RefreshPairedPlayers();
    }

    public List<ICharacter> RefreshPairedPlayers()
    {
        _lastCacheUpdate = DateTime.UtcNow;
        var result = new List<ICharacter>();

        if (!IsReady())
        {
            PluginLog.Debug("Mare is not available, returning empty list.");
            _cachedPairedPlayers = result;
            return result;
        }

        try
        {
            var handledAddresses = GetHandledAddressesIpc?.Invoke() ?? new List<nint>();
            result = handledAddresses
                .Select(addr => Svc.Objects.FirstOrDefault(obj => obj.Address == addr))
                .OfType<ICharacter>()
                .Where(c => c.IsValid())
                .ToList();

            PluginLog.Debug($"Mare IPC returned {handledAddresses.Count} addresses, {result.Count} valid characters");
        } catch (Exception e)
        {
            PluginLog.Error($"An exception occurred while getting Mare handled addresses via IPC.\n{e}");
            result = new List<ICharacter>();
        }

        if (!ArePlayerListsEqual(_cachedPairedPlayers, result))
        {
            PluginLog.Debug($"Mare player list changed: {_cachedPairedPlayers.Count} -> {result.Count} players");
            _cachedPairedPlayers = result;
        }

        return _cachedPairedPlayers;
    }

    public object? GetCharacterData(ICharacter character)
    {
        if (!IsReady())
        {
            PluginLog.Debug($"Mare not available when trying to get data for {character.Name.TextValue}");
            return null;
        }

        return Svc.Framework.RunOnFrameworkThread(() =>
        {
            Initialize();
            if (!_isInitialized)
            {
                PluginLog.Warning(
                    $"Mare IPC not properly initialized, cannot get data for {character.Name.TextValue}.");
                return null;
            }

            var pairObject = GetMarePair(character);
            if (pairObject == null)
            {
                PluginLog.Debug($"No Mare pair found for character {character.Name.TextValue}.");
                return null;
            }

            var characterData = pairObject.GetFoP("LastReceivedCharacterData");
            PluginLog.Debug(
                $"Successfully retrieved Mare data for {character.Name.TextValue}: {(characterData != null ? "Found" : "Null")}");
            return characterData;
        }).Result;
    }

    public string? GetFileCachePath(string hash)
    {
        if (!IsReady()) return null;

        Initialize();
        if (!_isInitialized || _fileCacheManager == null || _getFileCacheByHashMethod == null) return null;

        try
        {
            var fileCacheEntityObject = _getFileCacheByHashMethod.Invoke(_fileCacheManager, new object[] { hash });
            return fileCacheEntityObject?.GetFoP("ResolvedFilepath") as string;
        } catch (Exception e)
        {
            PluginLog.Error($"An exception occurred while reflecting into Mare Synchronos for file cache path.\n{e}");
            return null;
        }
    }

    private void Initialize()
    {
        if (_isInitialized) return;

        PluginLog.Debug("[Mare IPC] Initializing...");

        if (!DalamudReflector.TryGetDalamudPlugin("MareSynchronos", out var marePlugin, true, true))
        {
            PluginLog.Debug("[Mare IPC] MareSynchronos plugin not found during initialization.");
            return;
        }

        var initializationSuccessful = false;

        try
        {
            var host = marePlugin.GetFoP("_host");
            if (host?.GetFoP("Services") is not IServiceProvider serviceProvider)
            {
                PluginLog.Warning("[Mare IPC] Could not get Services from _host. Mare may still be loading.");
                return;
            }

            var pairManagerType = marePlugin.GetType().Assembly.GetType("MareSynchronos.PlayerData.Pairs.PairManager");
            if (pairManagerType != null)
            {
                _pairManager = serviceProvider.GetService(pairManagerType);
                if (_pairManager == null) PluginLog.Warning("[Mare IPC] Could not get PairManager service.");
            }

            var fileCacheManagerType =
                marePlugin.GetType().Assembly.GetType("MareSynchronos.FileCache.FileCacheManager");
            if (fileCacheManagerType != null)
            {
                _fileCacheManager = serviceProvider.GetService(fileCacheManagerType);
                if (_fileCacheManager != null)
                {
                    _getFileCacheByHashMethod =
                        fileCacheManagerType.GetMethod("GetFileCacheByHash", new[] { typeof(string) });
                    if (_getFileCacheByHashMethod == null)
                        PluginLog.Warning("[Mare IPC] Could not find method GetFileCacheByHash in FileCacheManager.");
                }
            }

            PluginLog.Information("[Mare IPC] Initialization complete.");
            initializationSuccessful = true;
        } catch (Exception e)
        {
            PluginLog.Error($"[Mare IPC] An exception occurred during initialization: {e}");
        }

        _isInitialized = initializationSuccessful;
    }

    private IDictionary? GetAllMareClientPairs()
    {
        Initialize();
        if (!_isInitialized || _pairManager == null) return null;

        try
        {
            return _pairManager.GetFoP("_allClientPairs") as IDictionary;
        } catch (Exception e)
        {
            PluginLog.Error($"An exception occurred while reflecting into Mare Synchronos to get client pairs.\n{e}");
            return null;
        }
    }

    private object? GetMarePair(ICharacter character)
    {
        var allClientPairs = GetAllMareClientPairs();
        if (allClientPairs == null) return null;

        try
        {
            foreach (var pairObject in allClientPairs.Values)
                if (pairObject.GetFoP("PlayerName") is string pairPlayerName &&
                    string.Equals(pairPlayerName, character.Name.TextValue, StringComparison.Ordinal))
                    return pairObject;
        } catch (Exception e)
        {
            PluginLog.Error(
                $"An exception occurred while processing Mare Synchronos pairs to find a specific pair.\n{e}");
        }

        return null;
    }

    protected override void OnPluginStateChanged(bool isAvailable, bool wasAvailable)
    {
        PluginLog.Information($"[Mare IPC] Plugin state changed: {wasAvailable} -> {isAvailable}. Resetting cache.");

        // Reset initialization state
        _isInitialized = false;
        _pairManager = null;
        _fileCacheManager = null;
        _getFileCacheByHashMethod = null;

        // Clear cached data
        _cachedPairedPlayers.Clear();
        _lastCacheUpdate = DateTime.MinValue;

        // Re-initialize EzIPC if plugin became available
        if (isAvailable) EzIPC.Init(this, identifier, SafeWrapper.AnyException);
    }

    private static bool ArePlayerListsEqual(List<ICharacter> list1, List<ICharacter> list2)
    {
        if (list1.Count != list2.Count) return false;

        var addresses1 = list1.Select(c => c.Address).OrderBy(a => a).ToList();
        var addresses2 = list2.Select(c => c.Address).OrderBy(a => a).ToList();

        return addresses1.SequenceEqual(addresses2);
    }
}
