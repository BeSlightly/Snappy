using Dalamud.Plugin;
using Snappy.Integrations;

namespace Snappy.Services;

public class IpcManager : IIpcManager, IDisposable
{
    private readonly List<IpcSubscriber> _allSubscribers;
    private readonly BrioIpc _brio;
    private readonly CustomizePlusIpc _customize;
    private readonly GlamourerIpc _glamourer;
    private readonly MareIpc _mare;
    private readonly PenumbraIpc _penumbra;

    public IpcManager()
    {
        _penumbra = new PenumbraIpc();
        _glamourer = new GlamourerIpc();
        _customize = new CustomizePlusIpc();
        _brio = new BrioIpc();
        _mare = new MareIpc();

        _allSubscribers = new List<IpcSubscriber> { _penumbra, _glamourer, _customize, _brio, _mare };

        // Subscribe to plugin list changes (from PR 2330)
        Svc.PluginInterface.ActivePluginsChanged += OnActivePluginsChanged;
        PluginLog.Information("[IpcManager] Subscribed to plugin list change events");
    }

    // Penumbra passthroughs
    public void PenumbraRemoveTemporaryCollection(int objIdx)
    {
        _penumbra.RemoveTemporaryCollection(objIdx);
    }

    public void PenumbraRedraw(int objIdx)
    {
        _penumbra.Redraw(objIdx);
    }

    public string GetMetaManipulations(int objIdx)
    {
        return _penumbra.GetMetaManipulations(objIdx);
    }

    public Dictionary<string, HashSet<string>> PenumbraGetGameObjectResourcePaths(int objIdx)
    {
        return _penumbra.GetGameObjectResourcePaths(objIdx);
    }

    public void PenumbraSetTempMods(ICharacter character, int? idx, Dictionary<string, string> mods, string manips)
    {
        _penumbra.SetTemporaryMods(character, idx, mods, manips);
    }

    // Glamourer passthroughs
    public string GetGlamourerState(ICharacter c)
    {
        return _glamourer.GetCharacterCustomization(c);
    }

    public void ApplyGlamourerState(string? base64, ICharacter c)
    {
        _glamourer.ApplyState(base64, c);
    }

    public void UnlockGlamourerState(IGameObject c)
    {
        _glamourer.UnlockState(c);
    }

    public void RevertGlamourerToAutomation(IGameObject c)
    {
        _glamourer.RevertToAutomation(c);
    }

    // CustomizePlus passthroughs
    public bool IsCustomizePlusAvailable()
    {
        return _customize.IsReady();
    }

    public string GetCustomizePlusScale(ICharacter c)
    {
        return _customize.GetScaleFromCharacter(c);
    }

    public Guid? SetCustomizePlusScale(IntPtr address, string scale)
    {
        return _customize.SetScale(address, scale);
    }

    public void RevertCustomizePlusScale(Guid profileId)
    {
        _customize.Revert(profileId);
    }

    // Brio passthroughs
    public string? GetBrioActorName(IGameObject actor)
    {
        return _brio.GetActorName(actor);
    }

    // Mare passthroughs
    public void SetUiOpen(bool isOpen)
    {
        _mare.SetUiOpen(isOpen);
    }

    public List<ICharacter> GetMarePairedPlayers()
    {
        return _mare.GetPairedPlayers();
    }

    public object? GetCharacterDataFromMare(ICharacter character)
    {
        return _mare.GetCharacterData(character);
    }

    public string? GetMareFileCachePath(string hash)
    {
        return _mare.GetFileCachePath(hash);
    }

    public Dictionary<string, bool> GetMarePluginStatus()
    {
        return _mare.GetMarePluginStatus();
    }

    public bool IsMarePairedAddress(nint address)
    {
        return _mare.IsHandledAddress(address);
    }

    public bool IsLightlessAddress(nint address)
    {
        return _mare.IsAddressHandledByLightless(address);
    }

    public bool IsPlayerSyncAddress(nint address)
    {
        return _mare.IsAddressHandledByPlayerSync(address);
    }

    public bool IsSnowcloakAddress(nint address)
    {
        return _mare.IsAddressHandledBySnowcloak(address);
    }


    public void Dispose()
    {
        try
        {
            Svc.PluginInterface.ActivePluginsChanged -= OnActivePluginsChanged;
            PluginLog.Debug("[IpcManager] Successfully unsubscribed from plugin list change events");
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"[IpcManager] Error unsubscribing from plugin list changes: {ex.Message}");
        }
    }

    private void OnActivePluginsChanged(IActivePluginsChangedEventArgs args)
    {
        try
        {
            PluginLog.Debug(
                $"[IpcManager] Plugin list changed: {args.Kind}, affected plugins: {string.Join(", ", args.AffectedInternalNames)}");

            // Notify all IPC subscribers about the plugin list change
            foreach (var subscriber in _allSubscribers) subscriber.HandlePluginListChanged(args.AffectedInternalNames);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"[IpcManager] Error handling plugin list change: {ex}");
        }
    }
}