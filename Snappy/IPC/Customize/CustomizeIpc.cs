using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
using ECommons.Logging;
using Snappy.Utils;

namespace Snappy.IPC.Customize;

public class CustomizeIpc : IDisposable
{
    private readonly ICallGateSubscriber<(int, int)> _getApiVersion;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> _getActiveProfileId;
    private readonly ICallGateSubscriber<Guid, (int, string?)> _getProfileById;
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> _setTempProfile;
    private readonly ICallGateSubscriber<Guid, int> _deleteTempProfileById;

    public CustomizeIpc()
    {
        _getApiVersion = Svc.PluginInterface.GetIpcSubscriber<(int, int)>(
            "CustomizePlus.General.GetApiVersion"
        );
        _getActiveProfileId = Svc.PluginInterface.GetIpcSubscriber<ushort, (int, Guid?)>(
            "CustomizePlus.Profile.GetActiveProfileIdOnCharacter"
        );
        _getProfileById = Svc.PluginInterface.GetIpcSubscriber<Guid, (int, string?)>(
            "CustomizePlus.Profile.GetByUniqueId"
        );
        _setTempProfile = Svc.PluginInterface.GetIpcSubscriber<ushort, string, (int, Guid?)>(
            "CustomizePlus.Profile.SetTemporaryProfileOnCharacter"
        );
        _deleteTempProfileById = Svc.PluginInterface.GetIpcSubscriber<Guid, int>(
            "CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId"
        );
    }

    public void Dispose() { }

    public bool CheckApi()
    {
        try
        {
            var version = _getApiVersion.InvokeFunc();
            if (version.Item1 >= 6)
            {
                PluginLog.Verbose($"Customize+ API v{version.Item1}.{version.Item2} found.");
                return true;
            }
            PluginLog.Warning(
                $"Customize+ API v{version.Item1}.{version.Item2} is not compatible. Snappy requires v6 or higher."
            );
            return false;
        }
        catch
        {
            PluginLog.Warning(
                "Could not check Customize+ API version. Is it installed and running?"
            );
            return false;
        }
    }

    public string GetScaleFromCharacter(ICharacter c)
    {
        if (!CheckApi())
            return string.Empty;

        try
        {
            var (profileIdCode, profileId) = _getActiveProfileId.InvokeFunc(c.ObjectIndex);
            if (profileIdCode != 0 || profileId == null || profileId == Guid.Empty)
            {
                PluginLog.Debug(
                    $"C+: No active profile found for {c.Name} (Code: {profileIdCode})."
                );
                return string.Empty;
            }
            PluginLog.Debug($"C+: Found active profile {profileId} for {c.Name}");

            var (profileDataCode, profileJson) = _getProfileById.InvokeFunc(profileId.Value);
            if (profileDataCode != 0 || string.IsNullOrEmpty(profileJson))
            {
                PluginLog.Warning(
                    $"C+: Could not retrieve profile data for {profileId} (Code: {profileDataCode})."
                );
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
        if (!CheckApi() || string.IsNullOrEmpty(scale))
            return null;

        var gameObj = Svc.Objects.CreateObjectReference(address);
        if (gameObj is ICharacter c)
        {
            try
            {
                PluginLog.Information(
                    $"C+ applying temporary profile to: {c.Name} ({c.Address:X})"
                );
                var (code, guid) = _setTempProfile.InvokeFunc(c.ObjectIndex, scale);
                PluginLog.Debug(
                    $"C+ SetTemporaryProfileOnCharacter result: Code={code}, Guid={guid}"
                );
                return guid;
            }
            catch (Exception ex)
            {
                PluginLog.Error($"Exception during C+ SetScale IPC.\n{ex}");
            }
        }
        return null;
    }

    public void Revert(Guid profileId)
    {
        if (!CheckApi() || profileId == Guid.Empty)
            return;

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
}
