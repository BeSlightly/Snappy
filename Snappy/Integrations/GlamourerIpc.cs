using System.Reflection;
using ECommons.Reflection;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;
using Snappy.Common;
using Snappy.Services;

namespace Snappy.Integrations;

public sealed class GlamourerIpc : IpcSubscriber
{
    private readonly ApplyState _apply;
    private readonly RevertToAutomation _revertToAutomation;
    private readonly UnlockState _unlockState;
    private readonly ApiVersion _version;

    private bool _reflectionSearched;
    private FieldInfo? _servicesField;
    private PropertyInfo? _providerProperty;
    private object? _serviceProvider;
    private object? _stateManager;
    private object? _designConverter;
    private object? _actorObjectManager;
    private FieldInfo? _actorObjectsField;
    private PropertyInfo? _objectManagerIndexer;
    private MethodInfo? _getOrCreateMethod;
    private MethodInfo? _shareBase64Method;
    private FieldInfo? _applicationRulesAllField;

    public GlamourerIpc() : base("Glamourer")
    {
        _version = new ApiVersion(Svc.PluginInterface);
        _apply = new ApplyState(Svc.PluginInterface);
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
        if (!IsPluginLoaded()) return string.Empty;

        try
        {
            PluginLog.Debug($"Getting customization for {c.Name} / {c.ObjectIndex}");
            if (TryGetStateViaReflection(c, out var reflected))
            {
                PluginLog.Debug($"Glamourer reflection succeeded for {c.Name.TextValue}.");
                return reflected;
            }
            PluginLog.Warning($"Glamourer reflection failed for {c.Name.TextValue}. Returning empty string.");
        }
        catch (Exception ex)
        {
            PluginLog.Warning("Glamourer IPC error: " + ex.Message);
        }

        PluginLog.Warning("Could not get character customization from Glamourer. Returning empty string.");
        return string.Empty;
    }

    public override bool IsReady()
    {
        try
        {
            var version = _version.Invoke();
            return version is { Major: 1, Minor: >= 4 } && IsPluginLoaded();
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

        if (isAvailable != wasAvailable)
        {
            ResetReflectionState();
            _reflectionSearched = false;
        }
    }

    private bool TryGetStateViaReflection(ICharacter c, out string base64)
    {
        base64 = string.Empty;

        InitializeReflection();
        if (_stateManager == null || _designConverter == null || _actorObjectManager == null ||
            _actorObjectsField == null || _objectManagerIndexer == null || _getOrCreateMethod == null ||
            _shareBase64Method == null || _applicationRulesAllField == null)
        {
            PluginLog.Debug("Glamourer reflection missing required members for base64 export.");
            return false;
        }

        var objectManager = _actorObjectsField.GetValue(_actorObjectManager);
        if (objectManager == null)
        {
            PluginLog.Debug("Glamourer reflection missing ObjectManager instance.");
            return false;
        }

        var actorObj = _objectManagerIndexer.GetValue(objectManager, new object[] { c.ObjectIndex });
        if (actorObj == null)
        {
            PluginLog.Debug($"Glamourer reflection failed to resolve actor index {c.ObjectIndex}.");
            return false;
        }

        var args = new object?[] { actorObj, null };
        var created = (bool)_getOrCreateMethod.Invoke(_stateManager, args)!;
        if (!created || args[1] == null)
        {
            PluginLog.Debug($"Glamourer reflection could not create state for {c.ObjectIndex}.");
            return false;
        }

        var rules = _applicationRulesAllField.GetValue(null);
        if (rules == null)
        {
            PluginLog.Debug("Glamourer reflection missing ApplicationRules.All.");
            return false;
        }

        base64 = _shareBase64Method.Invoke(_designConverter, new[] { args[1], rules }) as string ?? string.Empty;
        if (string.IsNullOrEmpty(base64))
            PluginLog.Debug($"Glamourer reflection returned empty base64 for {c.ObjectIndex}.");
        return !string.IsNullOrEmpty(base64);
    }

    private void InitializeReflection()
    {
        if (_reflectionSearched) return;

        try
        {
            if (!DalamudReflector.TryGetDalamudPlugin("Glamourer", out var plugin, false, true))
            {
                PluginLog.Debug("Glamourer reflection could not find plugin instance.");
                _reflectionSearched = true;
                return;
            }

            var pluginType = plugin.GetType();
            var pluginAssembly = pluginType.Assembly;

            _servicesField = pluginType.GetField("_services", BindingFlags.Instance | BindingFlags.NonPublic);
            var serviceManager = _servicesField?.GetValue(plugin);
            if (serviceManager == null)
            {
                PluginLog.Debug("Glamourer reflection could not access ServiceManager.");
                _reflectionSearched = true;
                return;
            }

            _providerProperty = serviceManager.GetType().GetProperty("Provider",
                BindingFlags.Instance | BindingFlags.Public);
            _serviceProvider = _providerProperty?.GetValue(serviceManager);
            if (_serviceProvider is not IServiceProvider provider)
            {
                PluginLog.Debug("Glamourer reflection could not access ServiceProvider.");
                _reflectionSearched = true;
                return;
            }

            var stateManagerType = pluginAssembly.GetType("Glamourer.State.StateManager");
            var designConverterType = pluginAssembly.GetType("Glamourer.Designs.DesignConverter");
            var applicationRulesType = pluginAssembly.GetType("Glamourer.Designs.ApplicationRules");
            var actorStateType = pluginAssembly.GetType("Glamourer.State.ActorState");
            var stateApiType = pluginAssembly.GetType("Glamourer.Api.StateApi");

            if (stateManagerType == null || designConverterType == null || applicationRulesType == null ||
                actorStateType == null || stateApiType == null)
            {
                PluginLog.Debug("Glamourer reflection missing core types.");
                _reflectionSearched = true;
                return;
            }

            var stateApi = provider.GetService(stateApiType);
            if (stateApi == null)
            {
                PluginLog.Debug("Glamourer reflection could not resolve StateApi service.");
                _reflectionSearched = true;
                return;
            }

            _stateManager = stateApiType.GetField("_stateManager", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(stateApi);
            _designConverter = stateApiType.GetField("_converter", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(stateApi);
            _actorObjectManager = stateApiType.GetField("_objects", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(stateApi);

            var actorObjectManagerType = _actorObjectManager?.GetType();
            _actorObjectsField = actorObjectManagerType?.GetField("Objects",
                BindingFlags.Instance | BindingFlags.Public);
            var objectManagerType = _actorObjectsField?.FieldType;
            _objectManagerIndexer = objectManagerType?.GetProperty("Item", new[] { typeof(int) });

            _getOrCreateMethod = stateManagerType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "GetOrCreate") return false;
                    var parameters = m.GetParameters();
                    return parameters.Length == 2 &&
                           parameters[0].ParameterType.FullName == "Penumbra.GameData.Interop.Actor" &&
                           parameters[1].ParameterType.IsByRef &&
                           parameters[1].ParameterType.GetElementType() == actorStateType;
                });

            _shareBase64Method = designConverterType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(m =>
                {
                    if (m.Name != "ShareBase64") return false;
                    var parameters = m.GetParameters();
                    return parameters.Length == 2 &&
                           parameters[0].ParameterType == actorStateType &&
                           parameters[1].ParameterType.IsByRef &&
                           parameters[1].ParameterType.GetElementType() == applicationRulesType;
                });

            _applicationRulesAllField =
                applicationRulesType.GetField("All", BindingFlags.Static | BindingFlags.Public);

            if (_stateManager == null || _designConverter == null || _actorObjectManager == null ||
                _actorObjectsField == null || _objectManagerIndexer == null || _getOrCreateMethod == null ||
                _shareBase64Method == null || _applicationRulesAllField == null)
                PluginLog.Debug("Glamourer reflection initialized but is missing required members.");

            _reflectionSearched = true;
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"Glamourer reflection initialization failed: {ex.Message}");
            ResetReflectionState();
            _reflectionSearched = true;
        }
    }

    private void ResetReflectionState()
    {
        _servicesField = null;
        _providerProperty = null;
        _serviceProvider = null;
        _stateManager = null;
        _designConverter = null;
        _actorObjectManager = null;
        _actorObjectsField = null;
        _objectManagerIndexer = null;
        _getOrCreateMethod = null;
        _shareBase64Method = null;
        _applicationRulesAllField = null;
    }
}
