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
    private System.Windows.Point _terminalTabDragOrigin;
    private TerminalSessionViewModel? _terminalTabDragSession;

    public MainWindow(MainViewModel viewModel, Services.TrayIcon tray)
    {
        InitializeComponent();
        DataContext = viewModel;
        IsVisibleChanged += (_, _) => viewModel.SetUiActive(IsVisible && WindowState != WindowState.Minimized);
        StateChanged += (_, _) => viewModel.SetUiActive(IsVisible && WindowState != WindowState.Minimized);
        _tray = tray;
        viewModel.PropertyChanged += Vm_PropertyChanged;
        ApplyTerminalLayout();
        Loaded += async (_, _) => { await RestoreLayoutAsync(); SyncGroqKeyBox(); };
        Closing += Window_Closing;
        StateChanged += (_, _) => UpdateMaximizeVisual();
        _tray.ShowRequested += (_, _) => RestoreFromTray();
        _tray.ToggleRequested += (_, _) => ToggleTrayVisibility();
        _tray.NewTerminalRequested += (_, _) => { RestoreFromTray(); if (Vm.NewSessionCommand.CanExecute(null)) Vm.NewSessionCommand.Execute(null); };
        _tray.ExitRequested += (_, _) => { _exiting = true; Close(); };
        _tray.RunningRequested += (_, _) => { RestoreFromTray(); Vm.CurrentPage = "Running"; };
        _tray.SettingsRequested += (_, _) => { RestoreFromTray(); Vm.CurrentPage = "Settings"; };
        _tray.ReopenRequested += (_, _) => { RestoreFromTray(); _ = Vm.ReopenTerminalAsync(); };
        _tray.StopAllRequested += (_, _) => { if (Vm.StopAllCommand.CanExecute(null)) Vm.StopAllCommand.Execute(null); };
        _tray.RescanRequested += (_, _) => { if (Vm.RescanCommand.CanExecute(null)) Vm.RescanCommand.Execute(null); };
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
        // WPF routes the vertical wheel to MouseWheel but silently drops WM_MOUSEHWHEEL, so a precision
        // touchpad's two-finger horizontal pan does nothing. Translate it into a horizontal scroll of
        // whatever scrollable ScrollViewer sits under the pointer (the split grid, the tab strip, …).
        if (msg == 0x020E /* WM_MOUSEHWHEEL */)
        {
            var delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
            if (Mouse.DirectlyOver is DependencyObject over && FindHorizontalScrollViewer(over) is { } scroller)
            {
                scroller.ScrollToHorizontalOffset(Math.Clamp(scroller.HorizontalOffset + delta / 120.0 * 48, 0, scroller.ScrollableWidth));
                handled = true;
            }
            return IntPtr.Zero;
        }

        // The custom title bar is client content. Keep it exactly inside this monitor's work
        // rectangle, without WindowChrome's maximized resize-frame inset being applied again.
        if (msg == 0x0083 /* WM_NCCALCSIZE */ && IsZoomed(hwnd))
        {
            var current = MonitorFromWindow(hwnd, 2);
            var bounds = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (current != IntPtr.Zero && GetMonitorInfo(current, ref bounds))
            {
                // Both NCCALCSIZE forms begin with the proposed client RECT.
                Marshal.StructureToPtr(bounds.rcWork, lParam, false);
                handled = true;
            }
            return IntPtr.Zero;
        }
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;
        var monitor = MonitorFromWindow(hwnd, 0x2 /* MONITOR_DEFAULTTONEAREST */);
        if (monitor != IntPtr.Zero)
        {
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;
            var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
            mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
            mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
            mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
            mmi.ptMinTrackSize.X = Math.Min(mmi.ptMinTrackSize.X, mmi.ptMaxSize.X);
            mmi.ptMinTrackSize.Y = Math.Min(mmi.ptMinTrackSize.Y, mmi.ptMaxSize.Y);
            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, int flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr hwnd, ref WindowPlacement placement);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    // CenterScreen always drops the window on the primary monitor, so on a multi-monitor setup it can
    // open behind whatever's on a screen you aren't looking at — "it's running but I can't see it".
    // Centre it on the monitor the cursor is on instead, and pull it to the foreground. Win32 works in
    // physical pixels across monitors of differing DPI, which the WPF Left/Top properties don't.
    private void BringToActiveMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        if (GetCursorPos(out var cursor))
        {
            var monitor = MonitorFromPoint(cursor, 0x2 /* MONITOR_DEFAULTTONEAREST */);
            var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                var wasMaximized = WindowState == WindowState.Maximized;
                if (wasMaximized) WindowState = WindowState.Normal;
                if (GetWindowRect(hwnd, out var r))
                {
                    var work = info.rcWork;
                    var x = work.Left + Math.Max(0, (work.Right - work.Left - (r.Right - r.Left)) / 2);
                    var y = work.Top + Math.Max(0, (work.Bottom - work.Top - (r.Bottom - r.Top)) / 2);
                    SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, 0x0001 /* NOSIZE */ | 0x0004 /* NOZORDER */);
                }
                if (wasMaximized) WindowState = WindowState.Maximized;
            }
        }
        Activate();
        Topmost = true;
        Topmost = false;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MinMaxInfo { public Point ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int cbSize; public Rect rcMonitor, rcWork; public int dwFlags; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowPlacement { public int length, flags, showCmd; public Point ptMinPosition, ptMaxPosition; public Rect rcNormalPosition; }

    private async Task RestoreLayoutAsync()
    {
        var placement = await Vm.GetPrefAsync("WindowPlacement");
        if (double.TryParse(await Vm.GetPrefAsync("SidebarWidth"), out var sw) && sw > 0) { _sidebarWidth = sw; SidebarColumn.Width = new GridLength(sw); }
        if (double.TryParse(await Vm.GetPrefAsync("DetailsWidth"), out var dw) && dw > 0) { _detailsWidth = dw; DetailsColumn.Width = new GridLength(dw); }
        if (double.TryParse(await Vm.GetPrefAsync("TerminalHeight"), out var th) && th > 80) _terminalHeight = th;
        // Collapsed panels are restored from the view model (loaded before the window was shown), but
        // its PropertyChanged fired before this window subscribed, so apply the two that drive columns.
        if (Vm.SidebarCollapsed) ApplySidebar();
        if (Vm.DetailsCollapsed) ApplyDetails();
        // Restore the window's position, size, maximized state and monitor at the Win32 level. WINDOWPLACEMENT
        // works in physical pixels and records which screen a maximized window lived on, so it survives mixed
        // DPI and multi-monitor setups that WPF's Left/Top/Width/Height mishandle. Skip under the offscreen
        // capture harness, which wants a predictable on-screen window.
        if (Environment.GetEnvironmentVariable("PUPPETEER_CAPTURE") is { Length: > 0 }) return;
        if (!string.IsNullOrEmpty(placement)) TryRestorePlacement(placement);
    }

    // Written as one awaited batch rather than a handful of fire-and-forget writes: this runs as the
    // window closes, and anything still in flight when the process exits is simply lost.
    private void SaveLayout()
    {
        var values = new Dictionary<string, string?>();
        if (CapturePlacement() is { } placement) values["WindowPlacement"] = placement;
        if (SidebarColumn.ActualWidth > 0) values["SidebarWidth"] = SidebarColumn.ActualWidth.ToString("F0");
        if (DetailsColumn.ActualWidth > 0) values["DetailsWidth"] = DetailsColumn.ActualWidth.ToString("F0");
        if (TerminalRow.ActualHeight > 80) values["TerminalHeight"] = TerminalRow.ActualHeight.ToString("F0");
        try { Vm.SavePrefsAsync(values).GetAwaiter().GetResult(); } catch { }
    }

    // rcNormalPosition is the non-maximized bounds even while maximized, and showCmd records whether the
    // window was maximized — so one blob captures size, position, monitor and maximized state together.
    private string? CapturePlacement()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return null;
        var wp = new WindowPlacement { length = Marshal.SizeOf<WindowPlacement>() };
        if (!GetWindowPlacement(hwnd, ref wp)) return null;
        var r = wp.rcNormalPosition;
        return string.Join(',', wp.showCmd, r.Left, r.Top, r.Right, r.Bottom);
    }

    private void TryRestorePlacement(string saved)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        var parts = saved.Split(',');
        if (parts.Length != 5 || !parts.All(p => int.TryParse(p, out _))) return;
        var v = parts.Select(int.Parse).ToArray();
        var rect = new Rect { Left = v[1], Top = v[2], Right = v[3], Bottom = v[4] };
        // A monitor that was present last session may be gone now. If the saved bounds no longer land on
        // any screen, leave WPF's CenterScreen default so the window doesn't open into the void.
        if (!IntersectsVirtualScreen(rect)) return;
        var wp = new WindowPlacement
        {
            length = Marshal.SizeOf<WindowPlacement>(),
            // 3 = SW_SHOWMAXIMIZED. Anything else (including a minimized/hidden last state) opens normally.
            showCmd = v[0] == 3 ? 3 : 1,
            rcNormalPosition = rect,
        };
        SetWindowPlacement(hwnd, ref wp);
        if (wp.showCmd == 3) WindowState = WindowState.Maximized;
    }

    private static bool IntersectsVirtualScreen(Rect r)
    {
        int vx = GetSystemMetrics(76), vy = GetSystemMetrics(77);
        int vw = GetSystemMetrics(78), vh = GetSystemMetrics(79);
        var left = Math.Max(r.Left, vx);
        var top = Math.Max(r.Top, vy);
        var right = Math.Min(r.Right, vx + vw);
        var bottom = Math.Min(r.Bottom, vy + vh);
        // Require a usable slab on screen, not a one-pixel sliver clinging to a monitor edge.
        return right - left >= 120 && bottom - top >= 80;
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

    private void FolderHide_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button { DataContext: FolderNode folder } && Vm.HideFolderProjectsCommand.CanExecute(folder))
            Vm.HideFolderProjectsCommand.Execute(folder);
        e.Handled = true;
    }

    // ---- Notification area ----



    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        BringToActiveMonitor();
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
        foreach (var session in Vm.Sessions.ToArray())
            if (session.Running) session.StopCommand.Execute(null);
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
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        // The confirm dialog is modal: Enter answers it, Escape cancels it, everything else is swallowed.
        if (Vm.DialogOpen)
        {
            if (e.Key == Key.Escape) { Vm.CancelDialog(); e.Handled = true; }
            else if (e.Key == Key.Enter) { if (Vm.DialogConfirmCommand.CanExecute(null)) Vm.DialogConfirmCommand.Execute(null); e.Handled = true; }
            return;
        }
        if (!Vm.ReaderOpen && !Vm.FieldEditorOpen && ctrl)
        {
            if (e.Key == Key.T)
            {
                if (shift) _ = Vm.ReopenTerminalAsync();
                else if (Vm.NewSessionCommand.CanExecute(null)) Vm.NewSessionCommand.Execute(null);
                e.Handled = true; return;
            }
            if (e.Key == Key.W)
            {
                if (Vm.SelectedSession is { } session) Vm.CloseSessionCommand.Execute(session);
                e.Handled = true; return;
            }
            if (e.Key == Key.Tab || e.Key == Key.PageDown || e.Key == Key.PageUp)
            {
                Vm.CycleTerminal(shift || e.Key == Key.PageUp ? -1 : 1);
                e.Handled = true; return;
            }
            if (e.Key >= Key.D1 && e.Key <= Key.D9)
            {
                Vm.SelectTerminal(e.Key == Key.D9 ? Vm.Sessions.Count - 1 : (int)e.Key - (int)Key.D1);
                e.Handled = true; return;
            }
            if (e.Key == Key.F)
            {
                SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; return;
            }
        }
        // Ctrl+` is the exception: toggling the panel away is exactly what you want from inside it.
        if (ctrl && TypingInTerminal && e.Key != Key.OemTilde) return;
        if (ctrl && e.Key == Key.K)
        {
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
            if (Vm.FieldEditorOpen) { Vm.CloseFieldEditorCommand.Execute(null); e.Handled = true; }
            else if (Vm.ReaderOpen) { Vm.CloseReaderCommand.Execute(null); e.Handled = true; }
            else if (!string.IsNullOrEmpty(Vm.ActiveSearch)) { Vm.ActiveSearch = ""; e.Handled = true; }
            else if (Vm.TerminalOpen) { Vm.TerminalOpen = false; e.Handled = true; }
        }
    }

    // Clicking the dimmed backdrop closes the reader, the way any other modal does. The card itself
    // marks the click handled so it never bubbles up to the backdrop's handler above it.
    private void ReaderOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm.CloseReaderCommand.CanExecute(null)) Vm.CloseReaderCommand.Execute(null);
    }

    private void ReaderModal_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void FieldEditorOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (Vm.CloseFieldEditorCommand.CanExecute(null)) Vm.CloseFieldEditorCommand.Execute(null);
    }

    private void FieldEditorModal_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    // Clicking the backdrop cancels the confirm dialog; clicking the card itself must not.
    private void DialogOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => Vm.CancelDialog();

    private void DialogModal_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void TerminalInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox box || box.DataContext is not TerminalSessionViewModel session) return;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && e.Key == Key.L)
        {
            session.ClearOutput();
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
        if (sender is not FrameworkElement pane) return;
        // Clicking anywhere in a pane makes it the selected session, so its border lights up and the
        // keyboard is aimed at its prompt.
        if (pane.DataContext is TerminalSessionViewModel session) Vm.SelectedSession = session;
        var input = FindVisualChild<TextBox>(pane);
        if (input is not { IsEnabled: true }) return;
        Dispatcher.BeginInvoke(() => input.Focus(), DispatcherPriority.Input);
    }

    // The tab strip is a plain ListBox over the same Sessions collection the split grid shows, so a
    // drag-reorder here keeps both views in sync. A click below the drag threshold still selects the tab.
    private void TerminalTab_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || FindVisualParent<Button>(source) is not null) return;
        _terminalTabDragOrigin = e.GetPosition(this);
        _terminalTabDragSession = FindVisualParent<ListBoxItem>(source)?.DataContext as TerminalSessionViewModel;
    }

    private void TerminalTab_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_terminalTabDragSession is null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - _terminalTabDragOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _terminalTabDragOrigin.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        var session = _terminalTabDragSession;
        _terminalTabDragSession = null;
        DragDrop.DoDragDrop((DependencyObject)sender, session, DragDropEffects.Move);
    }

    private void TerminalTab_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TerminalSessionViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void TerminalTab_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TerminalSessionViewModel)) is not TerminalSessionViewModel moving) return;
        var sessions = Vm.Sessions;
        var from = sessions.IndexOf(moving);
        if (from < 0) return;
        var to = e.OriginalSource is DependencyObject source && FindVisualParent<ListBoxItem>(source)?.DataContext is TerminalSessionViewModel target
            ? sessions.IndexOf(target) : sessions.Count - 1;
        if (to < 0 || from == to) return;
        sessions.Move(from, to);
        Vm.SelectedSession = moving;
        e.Handled = true;
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

    // The nearest ancestor that actually has horizontal content to scroll and is allowed to.
    private static ScrollViewer? FindHorizontalScrollViewer(DependencyObject? source)
    {
        for (var current = source; current is not null; current = ParentOf(current))
            if (current is ScrollViewer viewer && viewer.ScrollableWidth > 0 && viewer.HorizontalScrollBarVisibility != ScrollBarVisibility.Disabled)
                return viewer;
        return null;
    }

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
