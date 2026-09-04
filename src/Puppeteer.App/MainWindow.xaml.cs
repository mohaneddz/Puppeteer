using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Puppeteer.App.ViewModels;

namespace Puppeteer.App;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    private double _sidebarWidth = 238;
    private double _detailsWidth = 346;
    private double _terminalHeight = 280;

    private readonly Services.TrayIcon _tray;
    private bool _exiting;
    private System.Windows.Point _terminalPaneDragOrigin;
    private TerminalSessionViewModel? _terminalPaneDragSession;

    public MainWindow(MainViewModel viewModel, Services.TrayIcon tray)
    {
        InitializeComponent();
        DataContext = viewModel;
        _tray = tray;
        viewModel.PropertyChanged += Vm_PropertyChanged;
        ApplyTerminalLayout();
        Loaded += async (_, _) => { await RestoreLayoutAsync(); SyncGroqKeyBox(); };
        Closing += Window_Closing;
        StateChanged += (_, _) => { UpdateMaximizeVisual(); ApplyMinimizeToTray(); };
        _tray.ShowRequested += (_, _) => RestoreFromTray();
        _tray.ToggleRequested += (_, _) => ToggleTrayVisibility();
        _tray.NewTerminalRequested += (_, _) => { RestoreFromTray(); if (Vm.NewSessionCommand.CanExecute(null)) Vm.NewSessionCommand.Execute(null); };
        _tray.ExitRequested += (_, _) => { _exiting = true; Close(); };
        _tray.SetRunningCount(0);
        if (Environment.GetEnvironmentVariable("PUPPETEER_CAPTURE") is { Length: > 0 } capturePath)
            Loaded += (_, _) => CaptureAndExit(capturePath);
    }

    // A borderless window (WindowStyle=None + WindowChrome) maximizes to the full monitor by default,
    // spilling under the taskbar and clipping the edges — which is why the status bar and card rows
    // fell off-screen. Constrain the maximized bounds to the monitor work area.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ((HwndSource)PresentationSource.FromVisual(this)!).AddHook(WindowProc);
        UpdateMaximizeVisual();
    }

    private void UpdateMaximizeVisual()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Data = (Geometry)FindResource(maximized ? "IconWindowRestore" : "IconWindowMaximize");
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
    }

    private const int WM_GETMINMAXINFO = 0x0024;

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;
        var monitor = MonitorFromWindow(hwnd, 0x2 /* MONITOR_DEFAULTTONEAREST */);
        if (monitor != IntPtr.Zero)
        {
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            GetMonitorInfo(monitor, ref info);
            var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
            mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
            mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
            mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int cbSize; public Rect rcMonitor, rcWork; public int dwFlags; }

    private async Task RestoreLayoutAsync()
    {
        var work = SystemParameters.WorkArea;
        if (double.TryParse(await Vm.GetPrefAsync("WindowWidth"), out var w) && w > 400) Width = Math.Min(w, work.Width);
        if (double.TryParse(await Vm.GetPrefAsync("WindowHeight"), out var h) && h > 300) Height = Math.Min(h, work.Height);
        if (double.TryParse(await Vm.GetPrefAsync("SidebarWidth"), out var sw) && sw > 0) { _sidebarWidth = sw; SidebarColumn.Width = new GridLength(sw); }
        if (double.TryParse(await Vm.GetPrefAsync("DetailsWidth"), out var dw) && dw > 0) { _detailsWidth = dw; DetailsColumn.Width = new GridLength(dw); }
        if (double.TryParse(await Vm.GetPrefAsync("TerminalHeight"), out var th) && th > 80) _terminalHeight = th;
        if (await Vm.GetPrefAsync("Maximized") == "1") WindowState = WindowState.Maximized;
    }

    // Written as one awaited batch rather than a handful of fire-and-forget writes: this runs as the
    // window closes, and anything still in flight when the process exits is simply lost.
    private void SaveLayout()
    {
        var restore = RestoreBounds;
        var values = new Dictionary<string, string?>
        {
            ["Maximized"] = WindowState == WindowState.Maximized ? "1" : "0",
        };
        if (!restore.IsEmpty)
        {
            values["WindowWidth"] = restore.Width.ToString("F0");
            values["WindowHeight"] = restore.Height.ToString("F0");
        }
        if (SidebarColumn.ActualWidth > 0) values["SidebarWidth"] = SidebarColumn.ActualWidth.ToString("F0");
        if (DetailsColumn.ActualWidth > 0) values["DetailsWidth"] = DetailsColumn.ActualWidth.ToString("F0");
        if (TerminalRow.ActualHeight > 80) values["TerminalHeight"] = TerminalRow.ActualHeight.ToString("F0");
        try { Vm.SavePrefsAsync(values).GetAwaiter().GetResult(); } catch { }
    }

    private void CaptureAndExit(string path)
    {
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.6) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var target = new System.Windows.Media.Imaging.RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            target.Render(this);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(target));
            using (var stream = System.IO.File.Create(path)) encoder.Save(stream);
            Application.Current.Shutdown();
        };
        timer.Start();
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.SidebarCollapsed): ApplySidebar(); break;
            case nameof(MainViewModel.DetailsCollapsed): ApplyDetails(); break;
            case nameof(MainViewModel.TerminalOpen):
            case nameof(MainViewModel.TerminalMaximized): ApplyTerminalLayout(); break;
            case nameof(MainViewModel.RunningCount): _tray.SetRunningCount(Vm.RunningCount); break;
            case nameof(MainViewModel.GroqApiKey): SyncGroqKeyBox(); break;
        }
    }

    // ---- Notification area ----

    /// <summary>Hides the window from the taskbar when it is minimized, if the user asked for that.
    /// The processes keep running; the tray icon is what says so.</summary>
    private void ApplyMinimizeToTray()
    {
        if (WindowState != WindowState.Minimized || !Vm.MinimizeToTray) return;
        Hide();
        if (!_announcedTray)
        {
            _announcedTray = true;
            _tray.Notify("Puppeteer is still running", "Find it in the notification area, or double-click the icon to bring it back.");
        }
    }

    private bool _announcedTray;

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void ToggleTrayVisibility()
    {
        if (IsVisible && WindowState != WindowState.Minimized)
        {
            SaveLayout();
            Hide();
        }
        else RestoreFromTray();
    }

    /// <summary>Start hidden when launched by the Run key with --tray, so logging in doesn't throw a
    /// window in the user's face.</summary>
    public void StartHidden()
    {
        _announcedTray = true;
        WindowState = WindowState.Minimized;
        Hide();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // "Close to tray" is a hide, not an exit — Quit from the tray menu is the way out.
        if (!_exiting && Vm.CloseToTray)
        {
            e.Cancel = true;
            SaveLayout();
            Hide();
            return;
        }
        if (!_exiting && Vm.ConfirmExitWithSessions && Vm.RunningCount > 0)
        {
            var answer = MessageBox.Show(this,
                $"{Vm.RunningCount} terminal session(s) are still running. Quitting will stop them.",
                "Quit Puppeteer?", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.OK) { e.Cancel = true; return; }
        }
        SaveLayout();
        _tray.Dispose();
        Application.Current.Shutdown();
    }

    private void ApplySidebar()
    {
        if (Vm.SidebarCollapsed)
        {
            if (SidebarColumn.ActualWidth > 0) _sidebarWidth = SidebarColumn.ActualWidth;
            SidebarColumn.Width = new GridLength(0);
        }
        else SidebarColumn.Width = new GridLength(_sidebarWidth);
    }

    private void ApplyDetails()
    {
        if (Vm.DetailsCollapsed)
        {
            if (DetailsColumn.ActualWidth > 0) _detailsWidth = DetailsColumn.ActualWidth;
            DetailsColumn.Width = new GridLength(0);
        }
        else DetailsColumn.Width = new GridLength(_detailsWidth);
    }

    private void ApplyTerminalLayout()
    {
        var open = Vm.TerminalOpen;
        var maximized = open && Vm.TerminalMaximized;
        TerminalSplitterRow.Height = open && !maximized ? GridLength.Auto : new GridLength(0);
        if (maximized)
        {
            PagesRow.Height = new GridLength(0);
            TerminalRow.Height = new GridLength(1, GridUnitType.Star);
        }
        else if (open)
        {
            PagesRow.Height = new GridLength(1, GridUnitType.Star);
            TerminalRow.Height = new GridLength(_terminalHeight);
            TerminalRow.MinHeight = 120;
        }
        else
        {
            PagesRow.Height = new GridLength(1, GridUnitType.Star);
            TerminalRow.Height = new GridLength(0);
            TerminalRow.MinHeight = 0;
        }
    }

    private void TerminalSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (TerminalRow.ActualHeight > 40) _terminalHeight = TerminalRow.ActualHeight;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        // Dragging a maximized window restores it first, the way a native title bar does, and keeps
        // the grabbed point under the cursor instead of snapping the window's left edge to it.
        if (WindowState == WindowState.Maximized)
        {
            var cursor = e.GetPosition(this);
            var ratio = ActualWidth > 0 ? cursor.X / ActualWidth : 0.5;
            WindowState = WindowState.Normal;
            var screen = PointToScreen(cursor);
            Left = screen.X - RestoreBounds.Width * ratio;
            Top = screen.Y - cursor.Y;
        }
        // DragMove throws if the button was already released between the event and this call.
        try { DragMove(); } catch (InvalidOperationException) { }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void RunningPill_Click(object sender, MouseButtonEventArgs e) => Vm.CurrentPage = "Running";

    // PasswordBox deliberately doesn't expose Password as a bindable property, so the masked field is
    // pushed to the view model by hand. Guarded so restoring the saved key doesn't echo back as an edit.
    private bool _syncingGroqKey;

    private void GroqKey_Changed(object sender, RoutedEventArgs e)
    {
        if (_syncingGroqKey) return;
        Vm.GroqApiKey = GroqKeyBox.Password;
    }

    private void SyncGroqKeyBox()
    {
        if (GroqKeyBox.Password == Vm.GroqApiKey) return;
        _syncingGroqKey = true;
        GroqKeyBox.Password = Vm.GroqApiKey;
        _syncingGroqKey = false;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths)
            if (System.IO.Directory.Exists(path))
                await Vm.AddRootPathAsync(path);
    }

    // True while the caret is in a terminal's command line. Ctrl+K, Ctrl+R and Ctrl+L all mean
    // something to a shell, so the app's own bindings must not swallow them there.
    private bool TypingInTerminal => Keyboard.FocusedElement is TextBox { DataContext: TerminalSessionViewModel };

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        // Ctrl+` is the exception: toggling the panel away is exactly what you want from inside it.
        if (ctrl && TypingInTerminal && e.Key != Key.OemTilde) return;
        if (ctrl && e.Key == Key.K)
        {
            Vm.CurrentPage = "Projects";
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.OemTilde)
        {
            if (Vm.SelectedSession is null && Vm.SelectedProject is not null) Vm.NewSessionCommand.Execute(null);
            else Vm.TerminalOpen = !Vm.TerminalOpen;
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.N)
        {
            if (Vm.NewSessionCommand.CanExecute(null)) Vm.NewSessionCommand.Execute(null);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.R)
        {
            if (Vm.RescanCommand.CanExecute(null)) Vm.RescanCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (!string.IsNullOrEmpty(Vm.Search)) { Vm.Search = ""; e.Handled = true; }
            else if (Vm.TerminalOpen) { Vm.TerminalOpen = false; e.Handled = true; }
        }
    }

    private void TerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not TerminalSessionViewModel session) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.L)
        {
            session.Output.Clear();
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Enter:
                if (session.SendCommand.CanExecute(null)) session.SendCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                session.HistoryPrevious();
                box.CaretIndex = box.Text.Length;
                e.Handled = true;
                break;
            case Key.Down:
                session.HistoryNext();
                box.CaretIndex = box.Text.Length;
                e.Handled = true;
                break;
        }
    }

    // The output area is intentionally selectable but not itself an input control. Treating a click
    // anywhere in a pane as intent to type keeps a shell immediately usable, including in split view.
    private void TerminalPane_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindVisualParent<Button>(source) is not null) return;
        if (sender is not DependencyObject pane) return;
        var input = FindVisualChild<TextBox>(pane);
        if (input is not { IsEnabled: true }) return;
        Dispatcher.BeginInvoke(() => input.Focus(), DispatcherPriority.Input);
    }

    // A split-pane header is a drag handle. The grid itself receives the drop and reorders its
    // backing session collection, so the session process and its output remain intact.
    private void TerminalPaneHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TerminalSessionViewModel session }) return;
        _terminalPaneDragOrigin = e.GetPosition(this);
        _terminalPaneDragSession = session;
    }

    private void TerminalPaneHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (_terminalPaneDragSession is null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _terminalPaneDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _terminalPaneDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var session = _terminalPaneDragSession;
        _terminalPaneDragSession = null;
        DragDrop.DoDragDrop((DependencyObject)sender, session, DragDropEffects.Move);
    }

    private static T? FindVisualParent<T>(DependencyObject source) where T : DependencyObject
    {
        for (DependencyObject? current = source; current is not null; current = ParentOf(current))
            if (current is T match) return match;
        return null;
    }

    private static DependencyObject? ParentOf(DependencyObject current) => current switch
    {
        Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(current),
        _ => LogicalTreeHelper.GetParent(current)
    };

    private static T? FindVisualChild<T>(DependencyObject source) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++)
        {
            var child = VisualTreeHelper.GetChild(source, i);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    // Auto-follow the tail of a terminal pane's output unless the user has scrolled up to read back.
    private void TerminalOutput_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentHeightChange <= 0 || sender is not ScrollViewer viewer) return;
        if (viewer.VerticalOffset >= viewer.ScrollableHeight - e.ExtentHeightChange - 4)
            viewer.ScrollToEnd();
    }
}
