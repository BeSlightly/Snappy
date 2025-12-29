using System.Collections;
using System.Reflection;
using ECommons.Reflection;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using Snappy.Services;

namespace Snappy.Integrations;

public sealed class PenumbraIpc : IpcSubscriber
{
    private readonly AddTemporaryMod _addTempMod;
    private readonly AssignTemporaryCollection _assignTempCollection;
    private readonly CreateTemporaryCollection _createTempCollection;
    private readonly DeleteTemporaryCollection _deleteTempCollection;
    private readonly GetEnabledState _enabled;
    private readonly GetCollectionForObject _getCollectionForObject;
    private readonly GetCollections _getCollections;
    private readonly GetMetaManipulations _getMeta;
    private readonly GetGameObjectResourcePaths _getResourcePaths;
    private readonly RedrawObject _redraw;
    private readonly Dictionary<int, Guid> _tempCollectionGuids = new();
    private DateTime _collectionCacheStamp = DateTime.MinValue;
    private HashSet<Guid> _persistentCollectionIds = [];

    public PenumbraIpc() : base("Penumbra")
    {
        _getMeta = new GetMetaManipulations(Svc.PluginInterface);
        _redraw = new RedrawObject(Svc.PluginInterface);
        _addTempMod = new AddTemporaryMod(Svc.PluginInterface);
        _createTempCollection = new CreateTemporaryCollection(Svc.PluginInterface);
        _deleteTempCollection = new DeleteTemporaryCollection(Svc.PluginInterface);
        _assignTempCollection = new AssignTemporaryCollection(Svc.PluginInterface);
        _enabled = new GetEnabledState(Svc.PluginInterface);
        _getResourcePaths = new GetGameObjectResourcePaths(Svc.PluginInterface);
        _getCollections = new GetCollections(Svc.PluginInterface);
        _getCollectionForObject = new GetCollectionForObject(Svc.PluginInterface);
    }

    public Dictionary<string, HashSet<string>> GetGameObjectResourcePaths(int objIdx)
    {
        if (!IsReady()) return new Dictionary<string, HashSet<string>>();

        try
        {
            var result = _getResourcePaths.Invoke((ushort)objIdx);
            return result.FirstOrDefault() ?? new Dictionary<string, HashSet<string>>();
        }
        catch (Exception e)
        {
            PluginLog.Error($"Error getting Penumbra resource paths for object index {objIdx}:\n{e}");
            return new Dictionary<string, HashSet<string>>();
        }
    }

    public Dictionary<string, string> GetCollectionResolvedFiles(int objIdx)
    {
        if (!IsReady()) return new Dictionary<string, string>();

        try
        {
            object? resolvedFiles = null;
            var fetched = Svc.Framework.RunOnFrameworkThread(() =>
            {
                resolvedFiles = TryGetResolvedFilesForObject(objIdx);
                return resolvedFiles != null;
            }).Result;

            if (!fetched || resolvedFiles == null)
                return new Dictionary<string, string>();

            return ConvertResolvedFiles(resolvedFiles);
        }
        catch (Exception e)
        {
            PluginLog.Error($"Error getting Penumbra collection cache for object index {objIdx}:\n{e}");
            return new Dictionary<string, string>();
        }
    }

    public bool HasTemporaryCollection(int objIdx)
    {
        if (!IsReady()) return false;

        try
        {
            var (valid, _, effectiveCollection) = _getCollectionForObject.Invoke(objIdx);
            if (!valid) return false;
            if (effectiveCollection.Id == Guid.Empty) return false;

            var persistentIds = GetPersistentCollectionIds();
            if (persistentIds.Count == 0) return false;
            return !persistentIds.Contains(effectiveCollection.Id);
        }
        catch (Exception e)
        {
            PluginLog.Error($"Error checking Penumbra collection for object index {objIdx}:\n{e}");
            return false;
        }
    }

    public void RemoveTemporaryCollection(int objIdx)
    {
        if (!IsReady()) return;

        if (!_tempCollectionGuids.TryGetValue(objIdx, out var guid))
        {
            PluginLog.Debug($"[Penumbra] No temporary collection GUID found for object index '{objIdx}' to remove.");
            return;
        }

        PluginLog.Information($"[Penumbra] Deleting temporary collection for object index {objIdx} (Guid: {guid})");
        var ret = _deleteTempCollection.Invoke(guid);
        PluginLog.Debug("[Penumbra] DeleteTemporaryCollection returned: " + ret);

        _tempCollectionGuids.Remove(objIdx);
    }

    public void Redraw(int objIdx)
    {
        if (IsReady()) _redraw.Invoke(objIdx);
    }

    public string GetMetaManipulations(int objIdx)
    {
        return IsReady() ? _getMeta.Invoke(objIdx) : string.Empty;
    }

    public void SetTemporaryMods(ICharacter character, int? idx, Dictionary<string, string> mods, string manips)
    {
        if (!IsReady() || idx == null) return;

        var name = $"Snap_{character.Name.TextValue}_{idx.Value}";
        var result = _createTempCollection.Invoke("Snappy", name, out var collection);
        PluginLog.Verbose($"Created temp collection: {result}, GUID: {collection}");

        if (result != PenumbraApiEc.Success)
        {
            PluginLog.Error($"Failed to create temporary collection: {result}");
            return;
        }

        _tempCollectionGuids[idx.Value] = collection;

        var assign = _assignTempCollection.Invoke(collection, idx.Value);
        PluginLog.Verbose("Assigned temp collection: " + assign);

        foreach (var m in mods)
            PluginLog.Verbose(m.Key + " => " + m.Value);

        var addModResult = _addTempMod.Invoke("Snap", collection, mods, manips, 0);
        PluginLog.Verbose("Set temp mods result: " + addModResult);
    }

    public override bool IsReady()
    {
        try
        {
            return _enabled.Invoke() && IsPluginLoaded();
        }
        catch
        {
            return false;
        }
    }

    protected override void OnPluginStateChanged(bool isAvailable, bool wasAvailable)
    {
        if (!isAvailable && wasAvailable)
        {
            // Plugin was unloaded, clear temporary collections
            PluginLog.Information("[Penumbra] Plugin unloaded, clearing temporary collections");
            _tempCollectionGuids.Clear();
            _persistentCollectionIds.Clear();
            _collectionCacheStamp = DateTime.MinValue;
        }
        else if (isAvailable && !wasAvailable)
        {
            PluginLog.Information("[Penumbra] Plugin loaded/reloaded");
        }
    }

    private HashSet<Guid> GetPersistentCollectionIds()
    {
        if (_collectionCacheStamp != DateTime.MinValue &&
            DateTime.UtcNow - _collectionCacheStamp < TimeSpan.FromSeconds(2))
        {
            return _persistentCollectionIds;
        }

        try
        {
            _persistentCollectionIds = _getCollections.Invoke().Keys.ToHashSet();
            _collectionCacheStamp = DateTime.UtcNow;
        }
        catch (Exception e)
        {
            PluginLog.Error($"Error getting Penumbra collections:\n{e}");
            _persistentCollectionIds.Clear();
            _collectionCacheStamp = DateTime.MinValue;
        }

        return _persistentCollectionIds;
    }

    private object? TryGetResolvedFilesForObject(int objIdx)
    {
        var (valid, _, effectiveCollection) = _getCollectionForObject.Invoke(objIdx);
        if (!valid || effectiveCollection.Id == Guid.Empty)
            return null;

        if (!TryGetCollectionManager(out var collectionManager, out _))
            return null;
        if (collectionManager == null)
            return null;

        var collection = TryGetCollectionById(collectionManager, effectiveCollection.Id);
        if (collection == null)
            return null;

        return collection.GetFoP("ResolvedFiles")
               ?? collection.GetType()
                   .GetProperty("ResolvedFiles", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                   ?.GetValue(collection);
    }

    private bool TryGetCollectionManager(out object? collectionManager, out Assembly? penumbraAssembly)
    {
        collectionManager = null;
        penumbraAssembly = null;

        if (!DalamudReflector.TryGetDalamudPlugin("Penumbra", out var plugin, false, true))
            return false;

        penumbraAssembly = plugin.GetType().Assembly;
        var services = plugin.GetFoP("_services");
        if (services == null)
        {
            PluginLog.Warning("[Penumbra] Could not access _services for collection cache reflection.");
            return false;
        }

        var getService = services.GetType().GetMethod("GetService", BindingFlags.Instance | BindingFlags.Public);
        var collectionManagerType = penumbraAssembly.GetType("Penumbra.Collections.Manager.CollectionManager");
        if (getService == null || collectionManagerType == null)
        {
            PluginLog.Warning("[Penumbra] Could not resolve CollectionManager for collection cache reflection.");
            return false;
        }

        collectionManager = getService.MakeGenericMethod(collectionManagerType).Invoke(services, null);
        return collectionManager != null;
    }

    private static object? TryGetCollectionById(object collectionManager, Guid id)
    {
        var storage = collectionManager.GetFoP("Storage");
        if (storage != null && TryInvokeCollectionById(storage, "ById", id, out var collection))
            return collection;

        var tempCollections = collectionManager.GetFoP("Temp");
        if (tempCollections != null && TryInvokeCollectionById(tempCollections, "CollectionById", id, out collection))
            return collection;

        return null;
    }

    private static bool TryInvokeCollectionById(object holder, string methodName, Guid id, out object? collection)
    {
        collection = null;
        var method = holder.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public);
        if (method == null) return false;

        var args = new object?[] { id, null };
        var result = method.Invoke(holder, args);
        if (result is bool success && success)
        {
            collection = args[1];
            return collection != null;
        }

        return false;
    }

    private static Dictionary<string, string> ConvertResolvedFiles(object resolvedFiles)
    {
        var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (resolvedFiles is not IEnumerable entries)
            return results;

        foreach (var entry in entries)
        {
            var entryType = entry.GetType();
            var key = entryType.GetProperty("Key")?.GetValue(entry);
            var value = entryType.GetProperty("Value")?.GetValue(entry);
            if (key == null || value == null)
                continue;

            var gamePath = key.ToString();
            if (string.IsNullOrEmpty(gamePath))
                continue;

            var pathObj = value.GetType().GetProperty("Path")?.GetValue(value) ?? value.GetFoP("Path");
            var resolvedPath = pathObj?.ToString();
            if (string.IsNullOrEmpty(resolvedPath))
                continue;

            results[gamePath] = resolvedPath;
        }

        return results;
    }
}
