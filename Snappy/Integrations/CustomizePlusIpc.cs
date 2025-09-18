using Dalamud.Plugin.Ipc;
using Snappy.Services;

namespace Snappy.Integrations;

public sealed class CustomizePlusIpc : IpcSubscriber
{
    private readonly ICallGateSubscriber<Guid, int> _deleteTempProfileById;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> _getActiveProfileId;
    private readonly ICallGateSubscriber<(int, int)> _getApiVersion;
    private readonly ICallGateSubscriber<Guid, (int, string?)> _getProfileById;
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> _setTempProfile;

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
    }

    public string GetScaleFromCharacter(ICharacter c)
    {
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

    public override bool IsReady()
    {
        try
        {
            var (major, minor) = _getApiVersion.InvokeFunc();
            return major >= 6 && IsPluginLoaded();
        }
        catch
        {
            return false;
        }
    }
}