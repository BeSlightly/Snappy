using System;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.DalamudServices;
using ECommons.Logging;
using Glamourer.Api.Enums;
using Glamourer.Api.Helpers;
using Glamourer.Api.IpcSubscribers;

namespace Snappy.IPC.Glamourer;

public partial class GlamourerIpc : IDisposable
{
    private readonly ApplyState _apply;
    private readonly GetStateBase64 _get;
    private readonly global::Glamourer.Api.IpcSubscribers.RevertToAutomation _revertToAutomation;
    private readonly UnlockState _unlockState;
    private readonly ApiVersion _version;

    private const uint SnappyLockKey = 0x534E4150;

    public GlamourerIpc()
    {
        _version = new ApiVersion(Svc.PluginInterface);
        _get = new GetStateBase64(Svc.PluginInterface);
        _apply = new ApplyState(Svc.PluginInterface);
        _revertToAutomation = new global::Glamourer.Api.IpcSubscribers.RevertToAutomation(
            Svc.PluginInterface
        );
        _unlockState = new UnlockState(Svc.PluginInterface);
    }

    public void Dispose() { }

    public void ApplyState(string? base64, ICharacter obj)
    {
        if (!Check() || string.IsNullOrEmpty(base64))
            return;

        var flags = ApplyFlag.Equipment | ApplyFlag.Customization | ApplyFlag.Lock;
        PluginLog.Verbose(
            $"Glamourer applying state with lock key {SnappyLockKey} for {obj.Address:X}"
        );
        _apply.Invoke(base64, obj.ObjectIndex, SnappyLockKey, flags);
    }

    public void UnlockState(IGameObject obj)
    {
        if (!Check() || obj == null || obj.Address == IntPtr.Zero)
            return;

        PluginLog.Information(
            $"Glamourer explicitly unlocking state for object index {obj.ObjectIndex} with key."
        );
        var result = _unlockState.Invoke(obj.ObjectIndex, SnappyLockKey);
        if (result != GlamourerApiEc.Success && result != GlamourerApiEc.NothingDone)
        {
            PluginLog.Warning(
                $"Failed to unlock Glamourer state for object index {obj.ObjectIndex}. Result: {result}"
            );
        }
    }

    public void RevertToAutomation(IGameObject obj)
    {
        if (!Check() || obj == null || obj.Address == IntPtr.Zero)
            return;

        PluginLog.Information(
            $"Glamourer reverting to automation for object index {obj.ObjectIndex}."
        );
        var revertResult = _revertToAutomation.Invoke(obj.ObjectIndex);
        if (revertResult != GlamourerApiEc.Success && revertResult != GlamourerApiEc.NothingDone)
        {
            PluginLog.Warning(
                $"Failed to revert to automation for object index {obj.ObjectIndex}. Result: {revertResult}"
            );
        }
    }

    public string GetCharacterCustomization(ICharacter c)
    {
        if (!Check())
            return string.Empty;

        try
        {
            PluginLog.Debug($"Getting customization for {c.Name} / {c.ObjectIndex}");
            (GlamourerApiEc ec, string? result) = _get.Invoke(c.ObjectIndex);
            if (!string.IsNullOrEmpty(result))
                return result;
        }
        catch (Exception ex)
        {
            PluginLog.Warning("Glamourer IPC error: " + ex.Message);
        }

        PluginLog.Warning(
            "Could not get character customization from Glamourer. Returning empty string."
        );
        return string.Empty;
    }

    private bool Check()
    {
        try
        {
            return _version.Invoke() is { Major: 1, Minor: >= 4 };
        }
        catch
        {
            PluginLog.Warning("Glamourer not available");
            return false;
        }
    }
}
