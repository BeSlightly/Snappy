using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using ECommons.GameHelpers;

namespace Snappy.Common.Utilities;

public static class ActorOwnershipUtil
{
    public static bool IsSelfOwnedPet(ICharacter actor)
    {
        if (actor == null || !actor.IsValid())
            return false;

        var localPlayer = Player.Object;
        if (localPlayer == null || !localPlayer.IsValid())
            return false;

        if (!IsPetKind(actor.ObjectKind))
            return false;

        var ownerId = actor.OwnerId;
        return ownerId != 0 && ownerId == localPlayer.EntityId;
    }

    private static bool IsPetKind(ObjectKind kind)
    {
        return kind == ObjectKind.Companion
               || kind == ObjectKind.BattleNpc;
    }
}
