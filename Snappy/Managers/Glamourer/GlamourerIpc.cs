using Dalamud.Plugin;
using Dalamud.Game.ClientState.Objects.Types;
using Glamourer.Api.Enums;
using System;
using System.Collections.Concurrent;
using Snappy.Utils;
using Glamourer.Api.Helpers;

// All state-related IPC subscribers are in this namespace.
using Glamourer.Api.IpcSubscribers;

namespace Snappy.Managers.Glamourer;

public partial class GlamourerIpc : IDisposable
{
    private readonly DalamudUtil _dalamudUtil;
    private readonly ConcurrentQueue<Action> _queue;
    private readonly Configuration _configuration;
    private readonly ApplyState _apply;
    private readonly RevertState _revertState; // Changed from by-name subscribers
    private readonly GetStateBase64 _get;
    private readonly ApiVersion _version;
    private readonly EventSubscriber<bool> _gposeSubscriber;
    private Func<ICharacter, string?>? _getBase64FromCharacter;
    private readonly IDalamudPluginInterface _pluginInterface;

    // A unique key for Snappy to use when locking/unlocking state.
    private const uint SnappyLockKey = 0x534E4150; // "SNAP" in ASCII

    public GlamourerIpc(IDalamudPluginInterface pi, DalamudUtil dalamudUtil, ConcurrentQueue<Action> queue)
    {
        _dalamudUtil = dalamudUtil;
        _pluginInterface = pi;
        _queue = queue;
        _configuration = pi.GetPluginConfig() as Configuration ?? new Configuration(); // Ensure configuration is available
        _version = new ApiVersion(pi);
        _get = new GetStateBase64(pi);
        _apply = new ApplyState(pi);
        _revertState = new RevertState(pi); // Use the by-index revert subscriber

        // Subscribe to the GPose event. This is the reliable way to detect leaving GPose.
        // We fully qualify the static class name to resolve the ambiguity with the delegate.
        _gposeSubscriber = global::Glamourer.Api.IpcSubscribers.GPoseChanged.Subscriber(pi, OnGPoseEvent);

        // Defer IPC hook via Framework.Update
        _dalamudUtil.FrameworkUpdate += WaitForGlamourer;
    }

    private void OnGPoseEvent(bool inGPose)
    {
        Logger.Debug($"Glamourer IPC received GPose event: {inGPose}");
        GPoseChanged?.Invoke(inGPose);
    }

    private void WaitForGlamourer()
    {
        try
        {
            if (_getBase64FromCharacter == null)
            {
                _getBase64FromCharacter = _pluginInterface
                    .GetIpcSubscriber<ICharacter, string?>("Glamourer.GetStateBase64FromCharacter")
                    .InvokeFunc!;
                Logger.Info("Glamourer IPC hooked!");
            }
        }
        catch
        {
            // Try again next frame
            return;
        }

        // IPC is now available, stop checking
        _dalamudUtil.FrameworkUpdate -= WaitForGlamourer;
    }



    public void Dispose()
    {
        _gposeSubscriber.Dispose();
        _dalamudUtil.FrameworkUpdate -= WaitForGlamourer;
    }

    public void ApplyState(string? base64, ICharacter obj)
    {
        if (!Check() || string.IsNullOrEmpty(base64)) return;

        // Combine the default flags with the Lock flag to prevent automation from overriding our change.
        var flags = ApplyFlag.Equipment | ApplyFlag.Customization | ApplyFlag.Lock;
        Logger.Verbose($"Glamourer applying state with lock key {SnappyLockKey} for {obj.Address:X}");
        // Apply by index is correct here because the character is guaranteed to be live.
        _apply.Invoke(base64, obj.ObjectIndex, SnappyLockKey, flags);
    }

    public void RevertState(IGameObject obj)
    {
        if (!Check()) return;

        if (obj == null || obj.Address == IntPtr.Zero)
        {
            Logger.Warn("Tried to revert character with Glamourer but the game object was invalid/gone.");
            return;
        }

        // We applied the state with SnappyLockKey, so we must revert with it.
        // RevertState with a key will unlock and revert to automation.
        var revertResult = _revertState.Invoke(obj.ObjectIndex, SnappyLockKey);
        Logger.Info($"Glamourer reverting state for object index {obj.ObjectIndex} using key. Result: {revertResult}");

        // CORRECTED LINE: Removed the check for 'NothingChanged'
        if (revertResult != GlamourerApiEc.Success)
        {
            Logger.Warn($"Failed to revert/unlock Glamourer state for object index {obj.ObjectIndex}. Result: {revertResult}");
        }
    }


    public string GetCharacterCustomization(IntPtr ptr)
    {
        if (!Check()) return SafeBase64(_configuration.FallBackGlamourerString);

        try
        {
            var gameObj = _dalamudUtil.CreateGameObject(ptr);
            if (gameObj is ICharacter c)
            {
                Logger.Debug($"Getting customization for {c.Name} / {c.ObjectIndex}");
                (GlamourerApiEc ec, string? result) = _get.Invoke(c.ObjectIndex);
                if (!string.IsNullOrEmpty(result)) return result;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Glamourer IPC error: " + ex.Message);
        }

        Logger.Warn("Falling back to stored base64");
        return SafeBase64(_configuration.FallBackGlamourerString);
    }

    private bool Check()
    {
        try
        {
            // Brio requires 1.4, let's keep it consistent.
            return _version.Invoke() is { Major: 1, Minor: >= 4 };
        }
        catch
        {
            Logger.Warn("Glamourer not available");
            return false;
        }
    }

    private string SafeBase64(string input)
    {
        try
        {
            var bytes = Convert.FromBase64String(input);
            return Convert.ToBase64String(bytes);
        }
        catch
        {
            return _configuration.FallBackGlamourerString;
        }
    }

    public string? GetClipboardGlamourerString(ICharacter character)
    {
        if (!Check() || _getBase64FromCharacter == null)
        {
            Logger.Warn("[GlamourerIpc] Clipboard IPC not available.");
            return SafeBase64(_configuration.FallBackGlamourerString);
        }

        try
        {
            var result = _getBase64FromCharacter(character);
            if (!string.IsNullOrEmpty(result))
                return result;
        }
        catch (Exception ex)
        {
            Logger.Warn($"[GlamourerIpc] Failed to get clipboard-style Glamourer string: {ex.Message}");
        }

        Logger.Warn("[GlamourerIpc] Glamourer string was null or empty, returning fallback.");
        return SafeBase64(_configuration.FallBackGlamourerString);
    }
}