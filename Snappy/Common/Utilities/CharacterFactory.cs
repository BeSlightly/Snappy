using System.Reflection;
using Dalamud.Game.ClientState.Objects.Enums;

namespace Snappy.Common.Utilities;

public static class CharacterFactory
{
    private static ConstructorInfo? _characterConstructor;

    private static void Initialize()
    {
        _characterConstructor ??= typeof(ICharacter).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            null, new[] { typeof(IntPtr) }, null)!;
    }

    private static ICharacter Character(IntPtr address)
    {
        Initialize();
        return (ICharacter)_characterConstructor?.Invoke(new object[] { address })!;
    }

    public static ICharacter? Convert(IGameObject? actor)
    {
        if (actor == null || actor.Address == IntPtr.Zero) return null;

        if (actor is ICharacter character) return character;

        return actor.ObjectKind switch
        {
            ObjectKind.BattleNpc or ObjectKind.Companion or ObjectKind.Retainer or ObjectKind.EventNpc => Character(
                actor.Address),
            _ => null
        };
    }
}
