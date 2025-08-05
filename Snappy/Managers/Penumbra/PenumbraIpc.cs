using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using Snappy.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Snappy.Managers.Penumbra;

public partial class PenumbraIpc : IDisposable
{
    private readonly DalamudUtil _dalamudUtil;
    private readonly ConcurrentQueue<Action> _queue;
    private readonly IDalamudPluginInterface _pi;
    private readonly Dictionary<int, Guid> _tempCollectionGuids = new();

    private readonly GetMetaManipulations _getMeta;
    private readonly RedrawObject _redraw;
    private readonly RemoveTemporaryMod _removeTempMod;
    private readonly AddTemporaryMod _addTempMod;
    private readonly CreateTemporaryCollection _createTempCollection;
    private readonly global::Penumbra.Api.IpcSubscribers.DeleteTemporaryCollection _deleteTempCollection;
    private readonly AssignTemporaryCollection _assignTempCollection;
    private readonly GetEnabledState _enabled;

    private readonly ResolvePlayerPath _resolvePlayerPath;
    private readonly ResolveGameObjectPath _resolveGameObjectPath;
    private readonly ReverseResolveGameObjectPath _reverseGameObjectPath;
    private readonly ReverseResolvePlayerPath _reversePlayerPath;

    public PenumbraIpc(IDalamudPluginInterface pi, DalamudUtil dalamudUtil, ConcurrentQueue<Action> queue)
    {
        _pi = pi;
        _dalamudUtil = dalamudUtil;
        _queue = queue;

        _getMeta = new GetMetaManipulations(pi);
        _redraw = new RedrawObject(pi);
        _removeTempMod = new RemoveTemporaryMod(pi);
        _addTempMod = new AddTemporaryMod(pi);
        _createTempCollection = new CreateTemporaryCollection(pi);
        _deleteTempCollection = new global::Penumbra.Api.IpcSubscribers.DeleteTemporaryCollection(pi);
        _assignTempCollection = new AssignTemporaryCollection(pi);
        _enabled = new GetEnabledState(pi);

        _resolvePlayerPath = new ResolvePlayerPath(pi);
        _resolveGameObjectPath = new ResolveGameObjectPath(pi);
        _reverseGameObjectPath = new ReverseResolveGameObjectPath(pi);
        _reversePlayerPath = new ReverseResolvePlayerPath(pi);
    }

    public void Dispose() { }

    public void RemoveTemporaryCollection(int objIdx)
    {
        if (!Check()) return;

        if (!_tempCollectionGuids.TryGetValue(objIdx, out var guid))
        {
            Logger.Warn($"[Penumbra] No temporary collection GUID found for object index '{objIdx}' to remove.");
            return;
        }

        Logger.Info($"[Penumbra] Deleting temporary collection for object index {objIdx} (Guid: {guid})");
        var ret = _deleteTempCollection.Invoke(guid);
        Logger.Debug("[Penumbra] DeleteTemporaryCollection returned: " + ret);

        _tempCollectionGuids.Remove(objIdx);
    }

    public void Redraw(int objIdx)
    {
        if (!Check()) return;
        _redraw.Invoke(objIdx, RedrawType.Redraw);
    }

    public void Redraw(IntPtr objPtr)
    {
        if (!Check()) return;
        _queue.Enqueue(() =>
        {
            var gameObj = _dalamudUtil.CreateGameObject(objPtr);
            if (gameObj != null)
            {
                Logger.Verbose("Redrawing " + gameObj);
            }
        });
    }

    public string GetMetaManipulations(int objIdx)
    {
        if (!Check()) return string.Empty;
        return _getMeta.Invoke(objIdx);
    }

    public void SetTemporaryMods(ICharacter character, int? idx, Dictionary<string, string> mods, string manips)
    {
        if (!Check() || idx == null) return;
        var name = "Snap_" + character.Name.TextValue + "_" + idx.Value;
        var collection = _createTempCollection.Invoke(name);
        Logger.Verbose("Created temp collection: " + collection);

        _tempCollectionGuids[idx.Value] = collection;

        var assign = _assignTempCollection.Invoke(collection, idx.Value, true);
        Logger.Verbose("Assigned temp collection: " + assign);

        foreach (var m in mods)
            Logger.Verbose(m.Key + " => " + m.Value);

        var result = _addTempMod.Invoke("Snap", collection, mods, manips, 0);
        Logger.Verbose("Set temp mods result: " + result);
    }

    public string ResolvePath(string path)
    {
        if (!Check()) return path;
        return _resolvePlayerPath.Invoke(path) ?? path;
    }

    public string ResolvePathObject(string path, int objIdx)
    {
        if (!Check()) return path;
        return _resolveGameObjectPath.Invoke(path, objIdx) ?? path;
    }

    public string[] ReverseResolveObject(string path, int objIdx)
    {
        if (!Check()) return new[] { path };
        var result = _reverseGameObjectPath.Invoke(path, objIdx);
        return result.Length > 0 ? result : new[] { path };
    }

    public string[] ReverseResolvePlayer(string path)
    {
        if (!Check()) return new[] { path };
        var result = _reversePlayerPath.Invoke(path);
        return result.Length > 0 ? result : new[] { path };
    }

    private bool Check()
    {
        try
        {
            return _enabled.Invoke();
        }
        catch
        {
            Logger.Warn("Penumbra not available");
            return false;
        }
    }
}