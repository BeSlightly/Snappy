// CustomizeIpc.cs
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Snappy.Utils;
using System;
using System.Collections.Concurrent;
using System.Text;

namespace Snappy.Managers.Customize;

public partial class CustomizeIpc : IDisposable
{
    private readonly DalamudUtil _dalamudUtil;

    private readonly ICallGateSubscriber<(int, int)> _getApiVersion;
    private readonly ICallGateSubscriber<ushort, (int, Guid?)> _getActiveProfileId;
    private readonly ICallGateSubscriber<Guid, (int, string?)> _getProfileById;
    private readonly ICallGateSubscriber<ushort, string, (int, Guid?)> _setTempProfile;
    private readonly ICallGateSubscriber<Guid, int> _deleteTempProfileById;

    public CustomizeIpc(IDalamudPluginInterface pi, DalamudUtil dalamudUtil)
    {
        _dalamudUtil = dalamudUtil;

        // Modern IPC definitions based on C+ API v6
        _getApiVersion = pi.GetIpcSubscriber<(int, int)>("CustomizePlus.General.GetApiVersion");
        _getActiveProfileId = pi.GetIpcSubscriber<ushort, (int, Guid?)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
        _getProfileById = pi.GetIpcSubscriber<Guid, (int, string?)>("CustomizePlus.Profile.GetByUniqueId");
        _setTempProfile = pi.GetIpcSubscriber<ushort, string, (int, Guid?)>("CustomizePlus.Profile.SetTemporaryProfileOnCharacter");
        _deleteTempProfileById = pi.GetIpcSubscriber<Guid, int>("CustomizePlus.Profile.DeleteTemporaryProfileByUniqueId");
    }

    public void Dispose() { }

    public bool CheckApi()
    {
        try
        {
            var version = _getApiVersion.InvokeFunc();
            // We need API version 6 or higher.
            if (version.Item1 >= 6)
            {
                // Let's not spam the log every time. A debug message is fine.
                Logger.Verbose($"Customize+ API v{version.Item1}.{version.Item2} found.");
                return true;
            }
            Logger.Warn($"Customize+ API v{version.Item1}.{version.Item2} is not compatible. Snappy requires v6 or higher.");
            return false;
        }
        catch
        {
            Logger.Warn("Could not check Customize+ API version. Is it installed and running?");
            return false;
        }
    }

    public string GetScaleFromCharacter(ICharacter c)
    {
        if (!CheckApi()) return string.Empty;

        try
        {
            // Step 1: Get the active profile ID for the character.
            var (profileIdCode, profileId) = _getActiveProfileId.InvokeFunc(c.ObjectIndex);
            if (profileIdCode != 0 || profileId == null || profileId == Guid.Empty)
            {
                Logger.Debug($"C+: No active profile found for {c.Name} (Code: {profileIdCode}).");
                return string.Empty;
            }
            Logger.Debug($"C+: Found active profile {profileId} for {c.Name}");

            // Step 2: Get the profile data (as JSON) using the ID.
            var (profileDataCode, profileJson) = _getProfileById.InvokeFunc(profileId.Value);
            if (profileDataCode != 0 || string.IsNullOrEmpty(profileJson))
            {
                Logger.Warn($"C+: Could not retrieve profile data for {profileId} (Code: {profileDataCode}).");
                return string.Empty;
            }

            // The result is the raw JSON, no Base64 encoding needed.
            return profileJson;
        }
        catch (Exception ex)
        {
            Logger.Error("Exception during C+ GetScaleFromCharacter IPC.", ex);
            return string.Empty;
        }
    }

    public Guid? SetScale(IntPtr address, string scale)
    {
        if (!CheckApi() || string.IsNullOrEmpty(scale)) return null;

        var gameObj = _dalamudUtil.CreateGameObject(address);
        if (gameObj is ICharacter c)
        {
            try
            {
                Logger.Info($"C+ applying temporary profile to: {c.Name} ({c.Address:X})");
                var (code, guid) = _setTempProfile.InvokeFunc(c.ObjectIndex, scale);
                Logger.Debug($"C+ SetTemporaryProfileOnCharacter result: Code={code}, Guid={guid}");
                return guid;
            }
            catch (Exception ex)
            {
                Logger.Error("Exception during C+ SetScale IPC.", ex);
            }
        }
        return null;
    }

    public void Revert(Guid profileId)
    {
        if (!CheckApi() || profileId == Guid.Empty) return;

        try
        {
            Logger.Info($"C+ reverting temporary profile for Guid: {profileId}");
            var code = _deleteTempProfileById.InvokeFunc(profileId);
            Logger.Debug($"C+ DeleteTemporaryProfileByUniqueId result: Code={code}");
        }
        catch (Exception ex)
        {
            Logger.Error("Exception during C+ Revert IPC.", ex);
        }
    }
}