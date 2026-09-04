using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using Application = System.Windows.Application;
using Font = System.Drawing.Font;
using FontStyle = System.Drawing.FontStyle;
using Icon = System.Drawing.Icon;

namespace Puppeteer.App.Services;

/// <summary>
/// The notification-area presence. WPF has no tray primitive, so this wraps the Win32 one that
/// WinForms already exposes; nothing else in the app touches WinForms.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _runningItem;

    public event EventHandler? ShowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? NewTerminalRequested;

    public TrayIcon()
    {
        _runningItem = new ToolStripMenuItem("No sessions running") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Open Puppeteer", null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty)) { Font = new Font(System.Drawing.SystemFonts.MenuFont!, FontStyle.Bold) });
        menu.Items.Add(new ToolStripMenuItem("New terminal", null, (_, _) => NewTerminalRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_runningItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _icon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Puppeteer",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Keeps the hover text and the menu's status line in step with what is actually running,
    /// so the tray answers "is anything still going?" without restoring the window.</summary>
    public void SetRunningCount(int count)
    {
        _runningItem.Text = count switch
        {
            0 => "No sessions running",
            1 => "1 session running",
            _ => $"{count} sessions running",
        };
        // NotifyIcon.Text is capped at 63 characters by the shell; these stay well inside it.
        _icon.Text = count > 0 ? $"Puppeteer — {count} running" : "Puppeteer";
    }

    public void Notify(string title, string message)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = message;
        _icon.ShowBalloonTip(3000);
    }

    // The app ships a PNG rather than an .ico, so rasterize it into an icon handle once at startup.
    private static Icon LoadIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.png"));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                using var bitmap = new Bitmap(stream);
                var handle = bitmap.GetHicon();
                using var raw = Icon.FromHandle(handle);
                return (Icon)raw.Clone();
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
