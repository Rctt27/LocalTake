using LocalTake.Core.Hotkeys;

namespace LocalTake.Core.Ports;

/// <summary>System-wide shortcuts. Actions run on the UI thread, outside the input handling that detected them.</summary>
public interface IGlobalHotkeys : IDisposable
{
    /// <returns>False when the system refused it, usually because another application holds it.</returns>
    bool Register(Hotkey hotkey, Action action);

    void UnregisterAll();
}
