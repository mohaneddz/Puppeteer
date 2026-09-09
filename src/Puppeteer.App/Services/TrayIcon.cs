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
    private readonly Icon _baseIcon;
    private Icon? _badgeIcon;
    private int _lastCount = -1;
    private bool _disposed;
    private readonly ToolStripMenuItem _stopItem;

    public event EventHandler? ShowRequested;
    public event EventHandler? ToggleRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? NewTerminalRequested;
    public event EventHandler? RunningRequested;
    public event EventHandler? ReopenRequested;
    public event EventHandler? StopAllRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? RescanRequested;

    public TrayIcon()
    {
        _runningItem = new ToolStripMenuItem("No sessions running", null, (_, _) => RunningRequested?.Invoke(this, EventArgs.Empty));
        _stopItem = new ToolStripMenuItem("Stop all running terminals", null, (_, _) => StopAllRequested?.Invoke(this, EventArgs.Empty)) { Enabled = false };

        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), BackColor = DarkMenuRenderer.Surface, ForeColor = DarkMenuRenderer.Text };
        menu.Items.Add(new ToolStripMenuItem("Open Puppeteer", null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty)) { Font = new Font(System.Drawing.SystemFonts.MenuFont!, FontStyle.Bold) });
        menu.Items.Add(new ToolStripMenuItem("New terminal", null, (_, _) => NewTerminalRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new ToolStripMenuItem("Reopen last closed terminal", null, (_, _) => ReopenRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new ToolStripMenuItem("Show / hide window", null, (_, _) => ToggleRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_runningItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new ToolStripMenuItem("Rescan project roots", null, (_, _) => RescanRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new ToolStripMenuItem("Settings", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Quit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

        _baseIcon = LoadIcon();
        _icon = new NotifyIcon
        {
            Icon = _baseIcon,
            Text = "Puppeteer",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ToggleRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>Keeps the hover text and the menu's status line in step with what is actually running,
    /// so the tray answers "is anything still going?" without restoring the window.</summary>
    public void SetRunningCount(int count)
    {
        if (_disposed || count == _lastCount) return;
        _lastCount = count;
        _stopItem.Enabled = count > 0;
        var old = _badgeIcon;
        _badgeIcon = count > 0 ? CreateBadge(count) : null;
        _icon.Icon = _badgeIcon ?? _baseIcon;
        old?.Dispose();
        _runningItem.Text = count switch
        {
            0 => "No sessions running",
            1 => "1 session running",
            _ => $"{count} sessions running",
        };
        // NotifyIcon.Text is capped at 63 characters by the shell; these stay well inside it.
        _icon.Text = count > 0 ? $"Puppeteer — {count} running" : "Puppeteer";
    }

    private Icon CreateBadge(int count)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.DrawIcon(_baseIcon, new Rectangle(0, 0, 32, 32));

        // A small disc pinned to the top-right corner, ringed with the icon's dark background so it
        // separates from whatever it overlaps instead of blending into the artwork underneath.
        const int size = 16;
        var badge = new Rectangle(32 - size - 1, 1, size, size);
        using (var ring = new SolidBrush(Color.FromArgb(24, 24, 24)))
            graphics.FillEllipse(ring, badge.X - 1, badge.Y - 1, size + 2, size + 2);
        using var fill = new SolidBrush(Color.FromArgb(240, 178, 74));
        graphics.FillEllipse(fill, badge);
        using var font = new Font("Segoe UI", count < 10 ? 11 : count < 100 ? 9 : 7, FontStyle.Bold, GraphicsUnit.Pixel);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        graphics.DrawString(count > 99 ? "99+" : count.ToString(), font, Brushes.Black, badge, format);
        return CloneIcon(bitmap);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon CloneIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try { using var raw = Icon.FromHandle(handle); return (Icon)raw.Clone(); }
        finally { DestroyIcon(handle); }
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
                return CloneIcon(bitmap);
            }
        }
        catch { }
        return (Icon)SystemIcons.Application.Clone();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _icon.Visible = false;
        var menu = _icon.ContextMenuStrip;
        _icon.Dispose();
        menu?.Dispose();
        _badgeIcon?.Dispose();
        _baseIcon.Dispose();
    }
}

/// <summary>Repaints the WinForms tray menu in the app's dark palette; the default renderer draws a
/// white background that clashes with everything else Puppeteer shows.</summary>
internal sealed class DarkMenuRenderer() : ToolStripProfessionalRenderer(new DarkColors())
{
    public static readonly Color Surface = Color.FromArgb(32, 33, 36);
    public static readonly Color Text = Color.FromArgb(228, 228, 230);
    private static readonly Color DisabledText = Color.FromArgb(120, 121, 125);

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : DisabledText;
        base.OnRenderItemText(e);
    }

    private sealed class DarkColors : ProfessionalColorTable
    {
        private static readonly Color Hover = Color.FromArgb(55, 56, 60);
        private static readonly Color Border = Color.FromArgb(64, 65, 70);

        public override Color ToolStripDropDownBackground => Surface;
        public override Color ImageMarginGradientBegin => Surface;
        public override Color ImageMarginGradientMiddle => Surface;
        public override Color ImageMarginGradientEnd => Surface;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color MenuItemPressedGradientBegin => Hover;
        public override Color MenuItemPressedGradientEnd => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
    }
}
