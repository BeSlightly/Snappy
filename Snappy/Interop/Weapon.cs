using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using System;
using System.Runtime.InteropServices;

namespace Snappy.Interop;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct Weapon
{
    [FieldOffset(0x18)] public IntPtr Parent;
    [FieldOffset(0x20)] public IntPtr NextSibling;
    [FieldOffset(0x28)] public IntPtr PreviousSibling;
    [FieldOffset(0xA8)] public WeaponDrawObject* WeaponRenderModel;
}

[StructLayout(LayoutKind.Explicit)]
public unsafe struct WeaponDrawObject
{
    [FieldOffset(0x00)] public RenderModel* RenderModel;
}

[StructLayout(LayoutKind.Explicit)]
public unsafe struct CharaExt
{
    [FieldOffset(0x0)] public Character Character;
    [FieldOffset(0x650)] public Character* Mount;
}
