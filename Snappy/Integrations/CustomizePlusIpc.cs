using System.Collections;
using System.Reflection;
using Dalamud.Plugin.Ipc;
using ECommons.Reflection;
using Snappy.Services;

namespace Snappy.Integrations;

public sealed class CustomizePlusIpc : IpcSubscriber
{
    private static readonly JsonSerializerSettings IpcProfileSerializerSettings = new()
        { DefaultValueHandling = DefaultValueHandling.Ignore };

    private readonly ICallGateSubscriber<Guid, int> _deleteTempProfileById;
    private readonly ICallGateSubscriber<ushort, int> _deleteTempProfileOnCharacter;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> _getActiveProfileId;
    private readonly ICallGateSubscriber<(int, int)> _getApiVersion;
    private readonly ICallGateSubscriber<Guid, (int, string?)> _getProfileById;
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> _setTempProfile;

    private bool _reflectionSearched;
    private FieldInfo? _servicesField;
    private PropertyInfo? _serviceProviderProperty;
    private object? _serviceProvider;
    private object? _profileManager;
    private object? _gameObjectService;
    private FieldInfo? _gameObjectServiceActorManagerField;
    private MethodInfo? _getActorByObjectIndexMethod;
    private MethodInfo? _getEnabledProfilesByActorMethod;
    private MethodInfo? _ipcProfileFromFullProfileMethod;

    public CustomizePlusIpc() : base("CustomizePlus")
    {
        _getApiVersion = Svc.PluginInterface.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
        _getActiveProfileId =
            Svc.PluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>(
                "CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
        _getProfileById =
            Svc.PluginInterface.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetByUniqueId");
        _setTempProfile =
            Svc.PluginInterface.GetIpcSubscriber<ushort, string, (int, Guid?)>(
                "CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
        _deleteTempProfileById =
            Svc.PluginInterface.GetIpcSubscriber<Guid, int>("CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId");
        _deleteTempProfileOnCharacter =
            Svc.PluginInterface.GetIpcSubscriber<ushort, int>("CustomizePlus.Profile.DeleteTemporaryProfileOnCharacter");
    }

    public string GetScaleFromCharacter(ICharacter c)
    {
        if (!IsPluginLoaded()) return string.Empty;

        if (TryGetScaleFromReflection(c, out var reflectionProfile))
            return reflectionProfile;

        if (!IsReady()) return string.Empty;

        try
        {
            var (profileIdCode, profileId) = _getActiveProfileId.InvokeFunc(c.ObjectIndex);
            if (profileIdCode != 0 || !profileId.HasValue || profileId.Value == Guid.Empty)
            {
                PluginLog.Debug($"C+: No active profile found for {c.Name} (Code: {profileIdCode}).");
                return string.Empty;
            }

            PluginLog.Debug($"C+: Found active profile {profileId} for {c.Name}");

            var (profileDataCode, profileJson) = _getProfileById.InvokeFunc(profileId.Value);
            if (profileDataCode != 0 || string.IsNullOrEmpty(profileJson))
            {
                PluginLog.Warning($"C+: Could not retrieve profile data for {profileId} (Code: {profileDataCode}).");
                return string.Empty;
            }

            return profileJson;
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Exception during C+ GetScaleFromCharacter IPC.\n{ex}");
            return string.Empty;
        }
    }

    public Guid? SetScale(IntPtr address, string scale)
    {
        if (!IsReady() || string.IsNullOrEmpty(scale)) return null;

        var gameObj = Svc.Objects.CreateObjectReference(address);
        if (gameObj is ICharacter c)
            try
            {
                PluginLog.Information($"C+ applying temporary profile to: {c.Name} ({c.Address:X})");
                var (code, guid) = _setTempProfile.InvokeFunc(c.ObjectIndex, scale);
                PluginLog.Debug($"C+ SetTemporaryProfileOnCharacter result: Code={code}, Guid={guid}");
                return guid;
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Exception during C+ SetScale IPC.\n{ex}");
            }

        return null;
    }

    public void Revert(Guid profileId)
    {
        if (!IsReady() || profileId == Guid.Empty) return;

        try
        {
            PluginLog.Information($"C+ reverting temporary profile for Guid: {profileId}");
            var code = _deleteTempProfileById.InvokeFunc(profileId);
            PluginLog.Debug($"C+ DeleteTemporaryProfileByUniqueId result: Code={code}");
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Exception during C+ Revert IPC.\n{ex}");
        }
    }

    public void DeleteTemporaryProfileOnCharacter(ushort objectIndex)
    {
        if (!IsReady()) return;

        try
        {
            var code = _deleteTempProfileOnCharacter.InvokeFunc(objectIndex);
            PluginLog.Debug($"C+ DeleteTemporaryProfileOnCharacter result: Code={code}");
        }
        catch (Exception ex)
        {
            PluginLog.Error($"Exception during C+ DeleteTemporaryProfileOnCharacter IPC.\n{ex}");
        }
    }

    public override bool IsReady()
    {
        try
        {
            var (major, minor) = _getApiVersion.InvokeFunc();
            return major >= 6 && IsPluginLoaded();
        }
        catch (Exception ex)
        {
            PluginLog.Verbose($"[CustomizePlus] IsReady check failed: {ex.Message}");
            return false;
        }
    }

    protected override void OnPluginStateChanged(bool isAvailable, bool wasAvailable)
    {
        if (isAvailable == wasAvailable) return;
        ResetReflectionState();
        _reflectionSearched = false;
    }

    private bool TryGetScaleFromReflection(ICharacter c, out string profileJson)
    {
        profileJson = string.Empty;

        InitializeReflection();
        if (_profileManager == null || _gameObjectService == null || _ipcProfileFromFullProfileMethod == null)
            return false;

        if (_getActorByObjectIndexMethod == null || _gameObjectServiceActorManagerField == null ||
            _getEnabledProfilesByActorMethod == null)
            return false;

        object? actor;
        try
        {
            actor = _getActorByObjectIndexMethod.Invoke(_gameObjectService, new object[] { (ushort)c.ObjectIndex });
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"C+ reflection: failed to get actor for index {c.ObjectIndex}: {ex.Message}");
            return false;
        }

        if (actor == null) return false;

        var actorManager = _gameObjectServiceActorManagerField.GetValue(_gameObjectService);
        if (actorManager == null) return false;

        var getIdentifierMethod = actor.GetType().GetMethod("GetIdentifier", BindingFlags.Instance | BindingFlags.Public);
        if (getIdentifierMethod == null) return false;

        object? actorIdentifier;
        try
        {
            actorIdentifier = getIdentifierMethod.Invoke(actor, new[] { actorManager });
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"C+ reflection: failed to get actor identifier for {c.ObjectIndex}: {ex.Message}");
            return false;
        }

        if (actorIdentifier == null) return false;

        object? profilesObj;
        try
        {
            profilesObj = _getEnabledProfilesByActorMethod.Invoke(_profileManager, new[] { actorIdentifier });
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"C+ reflection: failed to query profiles for {c.ObjectIndex}: {ex.Message}");
            return false;
        }

        if (profilesObj is not IEnumerable profiles) return false;

        object? profile = null;
        foreach (var entry in profiles)
        {
            profile = entry;
            break;
        }

        if (profile == null) return false;

        object? ipcProfile;
        try
        {
            ipcProfile = _ipcProfileFromFullProfileMethod.Invoke(null, new[] { profile });
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"C+ reflection: failed to convert profile for {c.ObjectIndex}: {ex.Message}");
            return false;
        }

        if (ipcProfile == null) return false;

        profileJson = JsonConvert.SerializeObject(ipcProfile, IpcProfileSerializerSettings);
        return !string.IsNullOrEmpty(profileJson);
    }

    private void InitializeReflection()
    {
        if (_reflectionSearched) return;

        try
        {
            if (!DalamudReflector.TryGetDalamudPlugin("CustomizePlus", out var plugin, false, true))
            {
                _reflectionSearched = true;
                return;
            }

            var pluginType = plugin.GetType();
            var pluginAssembly = pluginType.Assembly;

            _servicesField = pluginType.GetField("_services", BindingFlags.Instance | BindingFlags.NonPublic);
            var serviceManager = _servicesField?.GetValue(plugin);
            if (serviceManager == null)
            {
                _reflectionSearched = true;
                return;
            }

            _serviceProviderProperty = serviceManager.GetType().GetProperty("Provider",
                BindingFlags.Instance | BindingFlags.Public);
            _serviceProvider = _serviceProviderProperty?.GetValue(serviceManager);
            if (_serviceProvider is not IServiceProvider serviceProvider)
            {
                _reflectionSearched = true;
                return;
            }

            var profileManagerType = pluginAssembly.GetType("CustomizePlus.Profiles.ProfileManager");
            var gameObjectServiceType = pluginAssembly.GetType("CustomizePlus.Game.Services.GameObjectService");
            var ipcProfileType = pluginAssembly.GetType("CustomizePlus.Api.Data.IPCCharacterProfile");

            if (profileManagerType == null || gameObjectServiceType == null || ipcProfileType == null)
            {
                _reflectionSearched = true;
                return;
            }

            _profileManager = serviceProvider.GetService(profileManagerType);
            _gameObjectService = serviceProvider.GetService(gameObjectServiceType);
            _ipcProfileFromFullProfileMethod =
                ipcProfileType.GetMethod("FromFullProfile", BindingFlags.Public | BindingFlags.Static);

            _getActorByObjectIndexMethod = gameObjectServiceType.GetMethod("GetActorByObjectIndex",
                new[] { typeof(ushort) });
            _gameObjectServiceActorManagerField =
                gameObjectServiceType.GetField("_actorManager", BindingFlags.Instance | BindingFlags.NonPublic);
            _getEnabledProfilesByActorMethod = profileManagerType.GetMethod("GetEnabledProfilesByActor",
                BindingFlags.Instance | BindingFlags.Public);

            _reflectionSearched = true;
        }
        catch (Exception ex)
        {
            PluginLog.Debug($"C+ reflection initialization failed: {ex.Message}");
            ResetReflectionState();
            _reflectionSearched = true;
        }
    }

    private void ResetReflectionState()
    {
        _servicesField = null;
        _serviceProviderProperty = null;
        _serviceProvider = null;
        _profileManager = null;
        _gameObjectService = null;
        _gameObjectServiceActorManagerField = null;
        _getActorByObjectIndexMethod = null;
        _getEnabledProfilesByActorMethod = null;
        _ipcProfileFromFullProfileMethod = null;
    }
}
