namespace Snappy.Services;

public interface IIpcManager : IDisposable
{
    void PenumbraRemoveTemporaryCollection(int objIdx);
    void PenumbraRedraw(int objIdx);
    string GetMetaManipulations(int objIdx);
    Dictionary<string, HashSet<string>> PenumbraGetGameObjectResourcePaths(int objIdx);
    Dictionary<string, string> PenumbraGetCollectionResolvedFiles(int objIdx);
    bool PenumbraHasTemporaryCollection(int objIdx);
    void PenumbraSetTempMods(ICharacter character, int? idx, Dictionary<string, string> mods, string manips);
    string GetGlamourerState(ICharacter c);
    void ApplyGlamourerState(string? base64, ICharacter c);
    void UnlockGlamourerState(IGameObject c);
    void RevertGlamourerToAutomation(IGameObject c);
    bool IsCustomizePlusAvailable();
    string GetCustomizePlusScale(ICharacter c);
    Guid? SetCustomizePlusScale(IntPtr address, string scale);
    void RevertCustomizePlusScale(Guid profileId);
    string? GetBrioActorName(IGameObject actor);
    void SetUiOpen(bool isOpen);
    List<ICharacter> GetMarePairedPlayers();
    object? GetCharacterDataFromMare(ICharacter character);
    string? GetMareFileCachePath(string hash);
    Dictionary<string, bool> GetMarePluginStatus();
    bool IsMarePairedAddress(nint address);
    bool IsLightlessAddress(nint address);
    bool IsPlayerSyncAddress(nint address);
    bool IsSnowcloakAddress(nint address);
}
