using System;
using System.Drawing;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace ShowTime;

/// <summary>
/// ShowTime.ico, also embedded as the exe's Win32 icon (ApplicationIcon) so Explorer and shortcuts show it.
/// </summary>
internal static class AppIcon
{
    // Names the assembly, so that the icon also loads when ShowTime.exe is not the entry assembly (tests).
    private static readonly Uri Uri = new("pack://application:,,,/ShowTime;component/Assets/ShowTime.ico");

    private static Icon? _icon;
    private static ImageSource? _imageSource;

    /// <summary>Tray icon, at the system small-icon size.</summary>
    public static Icon Icon => _icon ??= CreateIcon();

    /// <summary>Window icon; WPF picks the best frame for the title bar and the taskbar.</summary>
    public static ImageSource ImageSource => _imageSource ??= BitmapFrame.Create(Uri);

    private static Icon CreateIcon()
    {
        using var stream = System.Windows.Application.GetResourceStream(Uri).Stream;
        return new Icon(stream, Forms.SystemInformation.SmallIconSize);
    }
}
