using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using ECommons.Reflection;
using Newtonsoft.Json;
using Snappy.Managers.Customize;
using Snappy.Managers.Glamourer;
using Snappy.Managers.Penumbra;
using Snappy.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Action = System.Action;

namespace Snappy.Managers;

public delegate void PenumbraRedrawEvent(IntPtr address, int objTblIdx);
public delegate void HeelsOffsetChange(float change);
public delegate void PenumbraResourceLoadEvent(IntPtr drawObject, string gamePath, string filePath);
public delegate void CustomizePlusScaleChange(string? scale);
public delegate void GPoseChange(bool inGPose);

public class IpcManager : IDisposable
{
    private readonly DalamudUtil _dalamudUtil;
    private readonly ConcurrentQueue<Action> actionQueue = new();

    private readonly PenumbraIpc _penumbra;
    private readonly GlamourerIpc _glamourer;
    private readonly CustomizeIpc _customize;

    public event GPoseChange? GPoseChanged;

    public IpcManager(IDalamudPluginInterface pi, DalamudUtil dalamudUtil)
    {
        Logger.Verbose("Creating IpcManager delegator");

        _dalamudUtil = dalamudUtil;
        _penumbra = new PenumbraIpc(pi, dalamudUtil, actionQueue);
        _glamourer = new GlamourerIpc(pi, dalamudUtil, actionQueue);
        _customize = new CustomizeIpc(pi, dalamudUtil);

        InitializeMareIpc();

        _glamourer.GPoseChanged += OnGPoseChanged;
        _dalamudUtil.FrameworkUpdate += HandleActionQueue;
        _dalamudUtil.ZoneSwitchEnd += () => actionQueue.Clear();
    }

    private void OnGPoseChanged(bool inGPose) => GPoseChanged?.Invoke(inGPose);

    private void HandleActionQueue()
    {
        if (actionQueue.TryDequeue(out var action) && action != null)
        {
            Logger.Debug("Execution action in queue: " + action.Method);
            action();
        }
    }

    public void Dispose()
    {
        _penumbra.Dispose();
        _glamourer.GPoseChanged -= OnGPoseChanged;
        _glamourer.Dispose();
        _customize.Dispose();
        _dalamudUtil.FrameworkUpdate -= HandleActionQueue;
        actionQueue.Clear();
    }

    // Penumbra passthroughs
    public void PenumbraRemoveTemporaryCollection(int objIdx) => _penumbra.RemoveTemporaryCollection(objIdx);
    public void PenumbraRedraw(int objIdx) => _penumbra.Redraw(objIdx);
    public void PenumbraRedraw(IntPtr objPtr) => _penumbra.Redraw(objPtr);
    public string GetMetaManipulations(int objIdx) => _penumbra.GetMetaManipulations(objIdx);
    public void PenumbraSetTempMods(ICharacter character, int? idx, Dictionary<string, string> mods, string manips) => _penumbra.SetTemporaryMods(character, idx, mods, manips);
    // Passthroughs for Penumbra path helpers
    public string PenumbraResolvePath(string path) => _penumbra.ResolvePath(path);
    public string PenumbraResolvePathObject(string path, int objIdx) => _penumbra.ResolvePathObject(path, objIdx);
    public string[] PenumbraReverseResolveObject(string path, int objIdx) => _penumbra.ReverseResolveObject(path, objIdx);
    public string[] PenumbraReverseResolvePlayer(string path) => _penumbra.ReverseResolvePlayer(path);


    // Glamourer passthroughs
    public GlamourerIpc GlamourerIpc => _glamourer;
    public string GetGlamourerState(ICharacter c) => _glamourer.GetCharacterCustomization(c.Address);

    // IPC subscribers for checking which plugin handles a character
    private ICallGateSubscriber<List<nint>>? lightlessSyncHandledAddresses;
    private ICallGateSubscriber<List<nint>>? snowcloakHandledAddresses;
    
    // Initialize IPC subscribers in constructor or initialization method
    private void InitializeMareIpc()
    {
        lightlessSyncHandledAddresses = Svc.PluginInterface.GetIpcSubscriber<List<nint>>("LightlessSync.GetHandledAddresses");
        snowcloakHandledAddresses = Svc.PluginInterface.GetIpcSubscriber<List<nint>>("MareSynchronos.GetHandledAddresses");
    }
    
    // Check which plugin is handling a specific character
    private string? GetHandlingPlugin(nint characterAddress)
    {
        // Check LightlessSync first
        if (lightlessSyncHandledAddresses?.HasFunction == true)
        {
            try
            {
                var lightlessAddresses = lightlessSyncHandledAddresses.InvokeFunc();
                if (lightlessAddresses.Contains(characterAddress))
                    return "LightlessSync";
            }
            catch { /* Ignore IPC errors */ }
        }
        
        // Check Snowcloak
        if (snowcloakHandledAddresses?.HasFunction == true)
        {
            try
            {
                var snowcloakAddresses = snowcloakHandledAddresses.InvokeFunc();
                if (snowcloakAddresses.Contains(characterAddress))
                    return "Snowcloak";
            }
            catch { /* Ignore IPC errors */ }
        }
        
        return null;
    }
    
    // Common private helper for Mare reflection
    private string GetMareData(ICharacter character, string dataPropertyName, string friendlyName)
    {
        // First, check which plugin is handling this specific character
        var handlingPlugin = GetHandlingPlugin(character.Address);
        
        if (handlingPlugin == "LightlessSync")
        {
            if (DalamudReflector.TryGetDalamudPlugin("LightlessSync", out var lightlessPlugin, true))
            {
                Logger.Debug($"Character {character.Name.TextValue} is handled by LightlessSync, getting {friendlyName}");
                return GetMareDataFromPlugin(character, dataPropertyName, friendlyName, lightlessPlugin, "LightlessSync");
            }
        }
        else if (handlingPlugin == "Snowcloak")
        {
            if (DalamudReflector.TryGetDalamudPlugin("Snowcloak", out var snowcloakPlugin, true))
            {
                Logger.Debug($"Character {character.Name.TextValue} is handled by Snowcloak, getting {friendlyName}");
                return GetMareDataFromPlugin(character, dataPropertyName, friendlyName, snowcloakPlugin, "MareSynchronos");
            }
        }
        
        // Fallback: try both plugins if character isn't specifically handled
        Logger.Debug($"Character {character.Name.TextValue} not found in handled addresses, trying fallback approach");
        
        // Check for LightlessSync first
        if (DalamudReflector.TryGetDalamudPlugin("LightlessSync", out var marePlugin, true))
        {
            Logger.Debug($"Attempting to get {friendlyName} from LightlessSync for {character.Name.TextValue}");
            var result = GetMareDataFromPlugin(character, dataPropertyName, friendlyName, marePlugin, "LightlessSync");
            if (!string.IsNullOrEmpty(result))
                return result;
        }
        
        // Check for Snowcloak if LightlessSync didn't return data
        if (DalamudReflector.TryGetDalamudPlugin("Snowcloak", out marePlugin, true))
        {
            Logger.Debug($"Attempting to get {friendlyName} from Snowcloak for {character.Name.TextValue}");
            return GetMareDataFromPlugin(character, dataPropertyName, friendlyName, marePlugin, "MareSynchronos");
        }
        
        Logger.Warn("Neither LightlessSync nor Snowcloak plugin found or loaded. Cannot reflect for data.");
        return string.Empty;
    }
    
    // Helper method to extract data from either plugin
    private string GetMareDataFromPlugin(ICharacter character, string dataPropertyName, string friendlyName, object marePlugin, string namespacePrefix)
    {
        string resultData = string.Empty;
        
        try
        {
            var host = marePlugin.GetFoP("_host");
            if (host == null)
            {
                Logger.Warn($"Reflection failed: Could not find _host in {namespacePrefix} plugin.");
                return string.Empty;
            }

            var serviceProvider = host.GetFoP("Services") as IServiceProvider;
            if (serviceProvider == null)
            {
                Logger.Warn("Reflection failed: Could not find Services IServiceProvider in _host.");
                return string.Empty;
            }

            var pairManagerType = marePlugin.GetType().Assembly.GetType($"{namespacePrefix}.PlayerData.Pairs.PairManager");
            if (pairManagerType == null)
            {
                Logger.Warn($"Reflection failed: Could not find type {namespacePrefix}.PlayerData.Pairs.PairManager.");
                return string.Empty;
            }

            var pairManager = serviceProvider.GetService(pairManagerType);
            if (pairManager == null)
            {
                Logger.Warn("Reflection failed: Could not get PairManager service from IServiceProvider.");
                return string.Empty;
            }

            var allClientPairs = pairManager.GetFoP("_allClientPairs") as System.Collections.IDictionary;
            if (allClientPairs == null)
            {
                Logger.Warn("Reflection failed: Could not find _allClientPairs in PairManager.");
                return string.Empty;
            }

            foreach (var pairObject in allClientPairs.Values)
            {
                var pairPlayerName = pairObject.GetFoP("PlayerName") as string;
                if (string.Equals(pairPlayerName, character.Name.TextValue, StringComparison.Ordinal))
                {
                    Logger.Debug($"Found matching pair for character {character.Name.TextValue}. Checking for {friendlyName} data.");

                    var lastReceivedCharacterData = pairObject.GetFoP("LastReceivedCharacterData");
                    if (lastReceivedCharacterData == null)
                    {
                        Logger.Debug($"LastReceivedCharacterData for {character.Name.TextValue} is null for this pair. This is normal if the user hasn't sent data yet. Continuing search...");
                        continue;
                    }

                    var dataDict = lastReceivedCharacterData.GetFoP(dataPropertyName) as System.Collections.IDictionary;
                    if (dataDict == null)
                    {
                        Logger.Warn($"Reflection failed: Could not find {dataPropertyName} dictionary in CharacterData.");
                        continue;
                    }

                    if (dataDict.Count == 0)
                    {
                        Logger.Debug($"{dataPropertyName} dictionary is empty for this pair.");
                        continue;
                    }

                    var objectKindEnum = dataDict.Keys.Cast<object>().First().GetType();
                    if (!objectKindEnum.IsEnum)
                    {
                        Logger.Warn($"Reflection failed: Reflected key type '{objectKindEnum.FullName}' is not an enum.");
                        return string.Empty;
                    }

                    var playerObjectKind = Enum.ToObject(objectKindEnum, 0); // ObjectKind.Player
                    Logger.Debug($"Searching for ObjectKind.Player key ({playerObjectKind}) in {dataPropertyName} dictionary.");

                    if (dataDict.Contains(playerObjectKind))
                    {
                        var dataJson = dataDict[playerObjectKind] as string;
                        if (!string.IsNullOrEmpty(dataJson))
                        {
                            Logger.Info($"SUCCESS: Retrieved {friendlyName} data from Mare for {character.Name.TextValue}.");
                            resultData = dataJson;
                            break;
                        }
                        else
                        {
                            Logger.Debug($"{friendlyName} data for Player object was present but null or empty.");
                        }
                    }
                    else
                    {
                        Logger.Debug($"{dataPropertyName} dictionary does not contain an entry for the Player object kind.");
                    }
                }
            }

            if (string.IsNullOrEmpty(resultData))
            {
                Logger.Debug($"No valid {friendlyName} data found in any matching Mare pairs for {character.Name.TextValue}.");
            }
        }
        catch (Exception e)
        {
            Logger.Error($"An exception occurred while reflecting into {namespacePrefix} for {friendlyName} data.", e);
        }

        return resultData;
    }

    public string GetGlamourerStateFromMare(ICharacter character)
    {
        return GetMareData(character, "GlamourerData", "Glamourer");
    }
    public void ApplyGlamourerState(string? base64, ICharacter c) => _glamourer.ApplyState(base64, c);
    public void RevertGlamourerState(IGameObject c) => _glamourer.RevertState(c);

    // CustomizePlus passthroughs
    public bool IsCustomizePlusAvailable() => _customize.CheckApi();
    public string GetCustomizePlusScale(ICharacter c) => _customize.GetScaleFromCharacter(c);
    public string GetCustomizePlusScaleFromMare(ICharacter character)
    {
        return GetMareData(character, "CustomizePlusData", "Customize+");
    }
    public Guid? SetCustomizePlusScale(IntPtr address, string scale) => _customize.SetScale(address, scale);
    public void RevertCustomizePlusScale(Guid profileId) => _customize.Revert(profileId);
}