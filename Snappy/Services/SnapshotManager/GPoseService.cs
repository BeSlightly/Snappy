using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Snappy.Services.SnapshotManager;

public class GPoseService : IGPoseService
{
    private readonly IActiveSnapshotManager _activeSnapshotManager;
    private Hook<ExitGPoseDelegate>? _exitGPoseHook;
    private bool _initialized;
    private bool _wasInGpose;

    public GPoseService(IActiveSnapshotManager activeSnapshotManager)
    {
        _activeSnapshotManager = activeSnapshotManager;
        Svc.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Svc.Framework.Update -= OnFrameworkUpdate;
        _exitGPoseHook?.Dispose();
    }

    public event Action? GPoseEntered;
    public event Action? GPoseExited;

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!_initialized) Initialize();

        var isInGpose = PluginUtil.IsInGpose();
        if (isInGpose && !_wasInGpose)
        {
            PluginLog.Debug("GPose entered.");
            OnGPoseEntered();
            GPoseEntered?.Invoke();
        }

        _wasInGpose = isInGpose;
    }

    private void OnGPoseEntered()
    {
        _activeSnapshotManager.OnGPoseEntered();
    }

    private unsafe void Initialize()
    {
        if (_initialized)
            return;

        try
        {
            var uiModule = Framework.Instance()->UIModule;
            var exitGPoseAddress = (IntPtr)uiModule->VirtualTable->ExitGPose;
            _exitGPoseHook = Svc.Hook.HookFromAddress<ExitGPoseDelegate>(
                exitGPoseAddress,
                ExitGPoseDetour
            );
            _exitGPoseHook.Enable();
            _initialized = true;
            PluginLog.Debug("GPoseService initialized with ExitGPose hook.");
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Failed to initialize GPoseService hook: {ex}");
        }
    }

    private unsafe void ExitGPoseDetour(UIModule* uiModule)
    {
        PluginLog.Debug("GPose exit detected via hook. Running automatic revert logic.");
        _activeSnapshotManager.RevertAllSnapshotsOnGposeExit();
        OnGPoseExited();
        GPoseExited?.Invoke();
        _exitGPoseHook!.Original(uiModule);
    }

    private void OnGPoseExited()
    {
        _activeSnapshotManager.OnGPoseExited();
    }

    private unsafe delegate void ExitGPoseDelegate(UIModule* uiModule);
}