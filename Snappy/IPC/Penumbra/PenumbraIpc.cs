using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.Logging;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;

namespace Snappy.IPC.Penumbra;

public class PenumbraIpc : IDisposable
{
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
    private readonly GetGameObjectResourcePaths _getResourcePaths;

    public PenumbraIpc()
    {
        _getMeta = new GetMetaManipulations(Svc.PluginInterface);
        _redraw = new RedrawObject(Svc.PluginInterface);
        _removeTempMod = new RemoveTemporaryMod(Svc.PluginInterface);
        _addTempMod = new AddTemporaryMod(Svc.PluginInterface);
        _createTempCollection = new CreateTemporaryCollection(Svc.PluginInterface);
        _deleteTempCollection = new global::Penumbra.Api.IpcSubscribers.DeleteTemporaryCollection(
            Svc.PluginInterface
        );
        _assignTempCollection = new AssignTemporaryCollection(Svc.PluginInterface);
        _enabled = new GetEnabledState(Svc.PluginInterface);

        _resolvePlayerPath = new ResolvePlayerPath(Svc.PluginInterface);
        _resolveGameObjectPath = new ResolveGameObjectPath(Svc.PluginInterface);
        _reverseGameObjectPath = new ReverseResolveGameObjectPath(Svc.PluginInterface);
        _reversePlayerPath = new ReverseResolvePlayerPath(Svc.PluginInterface);
        _getResourcePaths = new GetGameObjectResourcePaths(Svc.PluginInterface);
    }

    public void Dispose() { }

    public Dictionary<string, HashSet<string>> GetGameObjectResourcePaths(int objIdx)
    {
        if (!Check())
            return new Dictionary<string, HashSet<string>>();
        try
        {
            var result = _getResourcePaths.Invoke((ushort)objIdx);
            return result.Length > 0 ? result[0] : new Dictionary<string, HashSet<string>>();
        }
        catch (Exception e)
        {
            PluginLog.Error(
                $"Error getting Penumbra resource paths for object index {objIdx}:\n{e}"
            );
            return new Dictionary<string, HashSet<string>>();
        }
    }

    public void RemoveTemporaryCollection(int objIdx)
    {
        if (!Check())
            return;

        if (!_tempCollectionGuids.TryGetValue(objIdx, out var guid))
        {
            PluginLog.Warning(
                $"[Penumbra] No temporary collection GUID found for object index '{objIdx}' to remove."
            );
            return;
        }

        PluginLog.Information(
            $"[Penumbra] Deleting temporary collection for object index {objIdx} (Guid: {guid})"
        );
        var ret = _deleteTempCollection.Invoke(guid);
        PluginLog.Debug("[Penumbra] DeleteTemporaryCollection returned: " + ret);

        _tempCollectionGuids.Remove(objIdx);
    }

    public void Redraw(int objIdx)
    {
        if (!Check())
            return;
        _redraw.Invoke(objIdx, RedrawType.Redraw);
    }

    public void Redraw(IntPtr objPtr)
    {
        if (!Check())
            return;

        var gameObj = Svc.Objects.CreateObjectReference(objPtr);
        if (gameObj != null)
        {
            _redraw.Invoke(gameObj.ObjectIndex, RedrawType.Redraw);
            PluginLog.Verbose("Redrawing " + gameObj.Name);
        }
    }

    public string GetMetaManipulations(int objIdx)
    {
        if (!Check())
            return string.Empty;
        return _getMeta.Invoke(objIdx);
    }

    public void SetTemporaryMods(
        ICharacter character,
        int? idx,
        Dictionary<string, string> mods,
        string manips
    )
    {
        if (!Check() || idx == null)
            return;
        var name = "Snap_" + character.Name.TextValue + "_" + idx.Value;
        var collection = _createTempCollection.Invoke(name);
        PluginLog.Verbose("Created temp collection: " + collection);

        _tempCollectionGuids[idx.Value] = collection;

        var assign = _assignTempCollection.Invoke(collection, idx.Value, true);
        PluginLog.Verbose("Assigned temp collection: " + assign);

        foreach (var m in mods)
            PluginLog.Verbose(m.Key + " => " + m.Value);

        var result = _addTempMod.Invoke("Snap", collection, mods, manips, 0);
        PluginLog.Verbose("Set temp mods result: " + result);
    }

    public string ResolvePath(string path)
    {
        if (!Check())
            return path;
        return _resolvePlayerPath.Invoke(path) ?? path;
    }

    public string ResolvePathObject(string path, int objIdx)
    {
        if (!Check())
            return path;
        return _resolveGameObjectPath.Invoke(path, objIdx) ?? path;
    }

    public string[] ReverseResolveObject(string path, int objIdx)
    {
        if (!Check())
            return new[] { path };
        var result = _reverseGameObjectPath.Invoke(path, objIdx);
        return result.Length > 0 ? result : new[] { path };
    }

    public string[] ReverseResolvePlayer(string path)
    {
        if (!Check())
            return new[] { path };
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
            PluginLog.Warning("Penumbra not available");
            return false;
        }
    }
}
