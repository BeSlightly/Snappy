using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;
using Snappy.Common;
using Snappy.Services;

namespace Snappy.Integrations;

public sealed class GlamourerIpc : IpcSubscriber
{
    private readonly ApplyState _apply;
    private readonly GetStateBase64 _getStateBase64;
    private readonly RevertToAutomation _revertToAutomation;
    private readonly UnlockState _unlockState;
    private readonly ApiVersion _version;

    public GlamourerIpc() : base("Glamourer")
    {
        _version = new ApiVersion(Svc.PluginInterface);
        _apply = new ApplyState(Svc.PluginInterface);
        _getStateBase64 = new GetStateBase64(Svc.PluginInterface);
        _revertToAutomation = new RevertToAutomation(Svc.PluginInterface);
        _unlockState = new UnlockState(Svc.PluginInterface);
    }

    public void ApplyState(string? base64, ICharacter obj)
    {
        if (!IsReady() || string.IsNullOrEmpty(base64)) return;

        try
        {
            PluginLog.Verbose(
                $"Glamourer applying state with lock key {Constants.GlamourerLockKey:X} for {obj.Address:X}");
            _apply.Invoke(base64, obj.ObjectIndex, Constants.GlamourerLockKey);
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to apply Glamourer state: {ex.Message}");
        }
    }

    public void UnlockState(IGameObject obj)
    {
        if (!IsReady() || obj.Address == IntPtr.Zero) return;

        PluginLog.Information($"Glamourer explicitly unlocking state for object index {obj.ObjectIndex} with key.");
        var result = _unlockState.Invoke(obj.ObjectIndex, Constants.GlamourerLockKey);
        if (result is not (GlamourerApiEc.Success or GlamourerApiEc.NothingDone))
            PluginLog.Warning($"Failed to unlock Glamourer state for object index {obj.ObjectIndex}. Result: {result}");
    }

    public void RevertToAutomation(IGameObject obj)
    {
        if (!IsReady() || obj.Address == IntPtr.Zero) return;

        PluginLog.Information($"Glamourer reverting to automation for object index {obj.ObjectIndex}.");
        var revertResult = _revertToAutomation.Invoke(obj.ObjectIndex);
        if (revertResult is not (GlamourerApiEc.Success or GlamourerApiEc.NothingDone))
            PluginLog.Warning(
                $"Failed to revert to automation for object index {obj.ObjectIndex}. Result: {revertResult}");
    }

    public string GetCharacterCustomization(ICharacter c)
    {
        if (!IsReady()) return string.Empty;

        try
        {
            PluginLog.Debug($"Getting customization for {c.Name} / {c.ObjectIndex}");
            var (code, base64) = _getStateBase64.Invoke(c.ObjectIndex);
            if (code != GlamourerApiEc.Success || string.IsNullOrEmpty(base64))
            {
                PluginLog.Warning(
                    $"Glamourer GetStateBase64 returned {code} for {c.Name.TextValue} (index {c.ObjectIndex}). Returning empty string.");
                return string.Empty;
            }

            return base64;
        }
        catch (Exception ex)
        {
            PluginLog.Warning("Glamourer IPC error: " + ex.Message);
            return string.Empty;
        }
    }

    public override bool IsReady()
    {
        try
        {
            var version = _version.Invoke();
            return version is { Major: 1, Minor: >= 8 } && IsPluginLoaded();
        }
        catch (Exception ex)
        {
            PluginLog.Verbose($"[Glamourer] IsReady check failed: {ex.Message}");
            return false;
        }
    }

    protected override void OnPluginStateChanged(bool isAvailable, bool wasAvailable)
    {
        if (isAvailable && !wasAvailable)
            PluginLog.Information("[Glamourer] Plugin loaded/reloaded");
        else if (!isAvailable && wasAvailable) PluginLog.Information("[Glamourer] Plugin unloaded");
    }
}
