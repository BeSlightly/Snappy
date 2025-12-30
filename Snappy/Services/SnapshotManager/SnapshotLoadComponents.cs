using System;

namespace Snappy.Services.SnapshotManager;

[Flags]
public enum SnapshotLoadComponents
{
    None = 0,
    Files = 1 << 0,
    Glamourer = 1 << 1,
    CustomizePlus = 1 << 2,
    All = Files | Glamourer | CustomizePlus
}
