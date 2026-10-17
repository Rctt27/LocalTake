namespace LocalTake.Platform.Windows;

/// <summary>The console of the terminal LocalTake was started from, which a windowed program does not get by itself.</summary>
public static class WindowsConsole
{
    /// <returns>False when there is no such console, as when LocalTake is started from a shortcut.</returns>
    public static bool AttachToParent() => Win32.AttachConsole(Win32.ATTACH_PARENT_PROCESS);
}
