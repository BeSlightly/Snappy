using System.Reflection;
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

    private bool _reflectionInitialized;
    private object? _stateManager;
    private object? _actorObjectManager;
    private FieldInfo? _actorObjectsField;
    private PropertyInfo? _objectsIndexer;
    private MethodInfo? _getOrCreateMethod;
    private FieldInfo? _combinationField;

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
            PluginLog.Verbose($"Glamourer applying state with lock key {Constants.GlamourerLockKey:X} for {obj.Address:X}");
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
            PluginLog.Warning($"Failed to revert to automation for object index {obj.ObjectIndex}. Result: {revertResult}");
    }

    public string GetCharacterCustomization(ICharacter c)
    {
        if (!IsReady()) return string.Empty;

        try
        {
            PluginLog.Debug($"Getting customization for {c.Name} / {c.ObjectIndex}");
            var (code, base64) = _getStateBase64.Invoke(c.ObjectIndex, TryGetLockKey(c.ObjectIndex));
            if (code == GlamourerApiEc.Success && !string.IsNullOrEmpty(base64))
                return base64;

            PluginLog.Warning($"Glamourer GetStateBase64 returned {code} for {c.Name.TextValue} (index {c.ObjectIndex}). Returning empty string.");
            return string.Empty;
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
        else if (!isAvailable && wasAvailable)
            PluginLog.Information("[Glamourer] Plugin unloaded");

        if (isAvailable != wasAvailable)
            ResetReflectionState();
    }

    private uint TryGetLockKey(int objectIndex)
    {
        try
        {
            InitializeReflection();
            if (_objectsIndexer == null || _getOrCreateMethod == null || _combinationField == null)
                return 0;

            var objectManager = _actorObjectsField!.GetValue(_actorObjectManager);
            var actor = objectManager == null ? null : _objectsIndexer.GetValue(objectManager, [objectIndex]);
            if (actor == null)
                return 0;

            var args = new object?[] { actor, null };
            if (_getOrCreateMethod.Invoke(_stateManager, args) is not true || args[1] == null)
                return 0;

            return _combinationField.GetValue(args[1]) is uint key ? key : 0;
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"Glamourer lock key reflection failed: {ex.Message}");
            return 0;
        }
    }

    private void InitializeReflection()
    {
        if (_reflectionInitialized) return;
        ResetReflectionState();

        try
        {
            if (!TryGetLoadedPluginInstance("Glamourer", out var plugin))
                return;

            var assembly = plugin.GetType().Assembly;
            var services = plugin.GetType().GetField("_services", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(plugin);
            var provider = services?.GetType().GetProperty("Provider", BindingFlags.Instance | BindingFlags.Public)?.GetValue(services) as IServiceProvider;
            var stateApiType = assembly.GetType("Glamourer.Api.StateApi");
            var stateApi = provider != null && stateApiType != null ? provider.GetService(stateApiType) : null;
            if (stateApi == null || stateApiType == null)
                return;

            _stateManager = stateApiType.GetField("_stateManager", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(stateApi);
            _actorObjectManager = stateApiType.GetField("_objects", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(stateApi);
            if (_stateManager == null || _actorObjectManager == null)
                return;

            var actorStateType = assembly.GetType("Glamourer.State.ActorState");
            _actorObjectsField = _actorObjectManager.GetType().GetField("Objects", BindingFlags.Instance | BindingFlags.Public);
            _objectsIndexer = _actorObjectsField?.FieldType.GetProperty("Item", [typeof(int)]);
            _combinationField = actorStateType?.GetField("Combination", BindingFlags.Instance | BindingFlags.Public);
            _getOrCreateMethod = _stateManager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m => m.Name == "GetOrCreate"
                    && m.GetParameters() is { Length: 2 } p
                    && p[1].IsOut && p[1].ParameterType.GetElementType() == actorStateType);

            var ready = _objectsIndexer != null && _getOrCreateMethod != null && _combinationField != null;
            if (!ready)
            {
                PluginLog.Debug("Glamourer lock key reflection incomplete; it will be retried.");
                ResetReflectionState();
                return;
            }

            _reflectionInitialized = true;
            PluginLog.Debug("Glamourer lock key reflection initialized successfully.");
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"Glamourer reflection initialization failed: {ex.Message}");
            ResetReflectionState();
        }
    }

    private void ResetReflectionState()
    {
        _reflectionInitialized = false;
        _stateManager = null;
        _actorObjectManager = null;
        _actorObjectsField = null;
        _objectsIndexer = null;
        _getOrCreateMethod = null;
        _combinationField = null;
    }
}
