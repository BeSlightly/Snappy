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
}
