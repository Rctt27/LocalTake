using System;
using System.IO;
using ShowTime.Core.Ports;
using Forms = System.Windows.Forms;

namespace ShowTime;

/// <summary>The notification-area icon, its menu and its balloons.</summary>
internal sealed class TrayIcon : INotifications, IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly IShell _shell;
    private string? _lastSavedPath;

    public TrayIcon(IShell shell)
    {
        _shell = shell;
        Menu = new Forms.ContextMenuStrip();
        _icon = new Forms.NotifyIcon
        {
            Icon = AppIcon.Icon,
            Text = "ShowTime",
            ContextMenuStrip = Menu,
            Visible = true
        };
        // Left click opens the same menu as right click; NotifyIcon only exposes that through a private method.
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                typeof(Forms.NotifyIcon).GetMethod("ShowContextMenu",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    ?.Invoke(_icon, null);
        };
        _icon.BalloonTipClicked += (_, _) => RevealLastSaved();
    }

    public Forms.ContextMenuStrip Menu { get; }

    public string Text
    {
        get => _icon.Text;
        set => _icon.Text = value;
    }

    public void FileSaved(string path)
    {
        _lastSavedPath = path;
        _icon.ShowBalloonTip(3000, "Capture enregistrée", path + "\nCliquez pour l'afficher dans l'Explorateur.", Forms.ToolTipIcon.None);
    }

    /// <summary>A balloon unrelated to a saved file: clicking it must not reveal an older capture.</summary>
    public void Info(string title, string text)
    {
        _lastSavedPath = null;
        _icon.ShowBalloonTip(2000, title, text, Forms.ToolTipIcon.None);
    }

    private void RevealLastSaved()
    {
        if (_lastSavedPath is not null && File.Exists(_lastSavedPath))
            _shell.Reveal(_lastSavedPath);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        Menu.Dispose();
    }
}
