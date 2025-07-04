using FFXIVClientStructs.FFXIV.Client.System.Resource;
using System.Runtime.InteropServices;

namespace Snappy.Interop;

[StructLayout(LayoutKind.Explicit)]
public unsafe struct ResourceHandle
{
    public const int SsoSize = 15;

    public byte* FileName()
    {
        if (FileNameLength > SsoSize)
        {
            return FileNameData;
        }

        fixed (byte** name = &FileNameData)
        {
            return (byte*)name;
        }
    }

    [FieldOffset(0x08)]
    public ResourceCategory Category;

    [FieldOffset(0x48)]
    public byte* FileNameData;

    [FieldOffset(0x58)]
    public int FileNameLength;
}