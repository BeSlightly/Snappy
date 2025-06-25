// PenumbraIpc.Defs.cs
using Snappy.Managers;
using Snappy.Utils;

namespace Snapper.Managers.Penumbra;

public partial class PenumbraIpc
{
    public event VoidDelegate? PenumbraModSettingChanged;
    public event VoidDelegate? PenumbraInitialized;
    public event VoidDelegate? PenumbraDisposed;
    public event PenumbraRedrawEvent? PenumbraRedrawEvent;
    public event PenumbraResourceLoadEvent? PenumbraResourceLoadEvent;
}