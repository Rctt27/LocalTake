using System;
using System.Diagnostics;
using System.IO;
using LocalTake.Core.Ports;
using Forms = System.Windows.Forms;

namespace LocalTake;

/// <summary>The notification-area icon, its menu and its balloons.</summary>
internal sealed class TrayIcon : INotifications, IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private string? _lastSavedPath;

    public TrayIcon()
    {
        Menu = new Forms.ContextMenuStrip();
        _icon = new Forms.NotifyIcon
        {
            Icon = AppIcon.Icon,
            Text = "LocalTake",
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
            Process.Start("explorer.exe", $"/select,\"{_lastSavedPath}\"");
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        Menu.Dispose();
    }
}
