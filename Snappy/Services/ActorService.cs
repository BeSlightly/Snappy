using ECommons.GameHelpers;
using Snappy.Common;
using Penumbra.GameData.Structs;

namespace Snappy.Services;

public class ActorService : IActorService
{
    private readonly IIpcManager _ipcManager;

    public ActorService(IIpcManager ipcManager)
    {
        _ipcManager = ipcManager;
    }

    public List<ICharacter> GetSelectableActors()
    {
        if (PluginUtil.IsInGpose())
        {
            var gposeActors = new List<ICharacter>();
            for (var i = ObjectIndex.GPosePlayer.Index;
                 i < ObjectIndex.GPosePlayer.Index + 42;
                 ++i)
            {
                var p = CharacterFactory.Convert(Svc.Objects[i]);
                if (p != null && p.IsValid()) gposeActors.Add(p);
            }

            return gposeActors;
        }

        var localPlayer = Player.Available ? new[] { Player.Object } : Enumerable.Empty<ICharacter>();
        var marePlayers = _ipcManager.GetMarePairedPlayers().Where(p => p.IsValid());

        return localPlayer
            .UnionBy(marePlayers, p => p.Address) // Combines lists, ensuring uniqueness by address
            .OrderBy(p => p.Address != (Player.Object?.Address ?? IntPtr.Zero)) // Puts local player first
            .ThenBy(p => p.Name.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}