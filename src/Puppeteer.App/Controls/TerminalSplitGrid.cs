using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Puppeteer.App.ViewModels;

namespace Puppeteer.App.Controls;

/// <summary>Hosts terminal panes in a compact, resizable grid. Unlike UniformGrid, it deliberately
/// uses a single row for two sessions and adds real splitters between every row and column.</summary>
public sealed class TerminalSplitGrid : Grid
{
    public static readonly DependencyProperty SessionsProperty = DependencyProperty.Register(
        nameof(Sessions), typeof(IEnumerable), typeof(TerminalSplitGrid),
        new PropertyMetadata(null, OnSessionsChanged));

    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(TerminalSplitGrid),
        new PropertyMetadata(0, (owner, _) => ((TerminalSplitGrid)owner).Rebuild()));

    private INotifyCollectionChanged? _collection;
    private readonly Dictionary<int, double> _rowSizes = [];
    private readonly Dictionary<int, double> _columnSizes = [];
    private readonly List<Grid> _paneRows = [];
    private TerminalSessionViewModel? _dragCandidate;
    private TerminalSessionViewModel? _maximized;
    private Point _dragStart;
    private bool _dragging;
    public static readonly DependencyProperty HorizontalScrollableProperty = DependencyProperty.Register(
        nameof(HorizontalScrollable), typeof(bool), typeof(TerminalSplitGrid),
        new PropertyMetadata(false, (owner, _) =>
        {
            var grid = (TerminalSplitGrid)owner;
            grid._columnSizes.Clear();
            grid.Rebuild();
        }));
    public static readonly DependencyProperty ViewportWidthProperty = DependencyProperty.Register(
        nameof(ViewportWidth), typeof(double), typeof(TerminalSplitGrid),
        new PropertyMetadata(0d, (owner, _) => { var grid = (TerminalSplitGrid)owner; if (grid._maximized is not null) grid.Rebuild(); else grid.FillViewportWidth(); }));
    public double ViewportWidth
    {
        get => (double)GetValue(ViewportWidthProperty);
        set => SetValue(ViewportWidthProperty, value);
    }
    public static readonly DependencyProperty ViewportHeightProperty = DependencyProperty.Register(
        nameof(ViewportHeight), typeof(double), typeof(TerminalSplitGrid),
        new PropertyMetadata(0d, (owner, _) => { var grid = (TerminalSplitGrid)owner; if (grid._maximized is not null) grid.Rebuild(); }));
    public double ViewportHeight
    {
        get => (double)GetValue(ViewportHeightProperty);
        set => SetValue(ViewportHeightProperty, value);
    }
    public bool HorizontalScrollable
    {
        get => (bool)GetValue(HorizontalScrollableProperty);
        set => SetValue(HorizontalScrollableProperty, value);
    }
    public static readonly DependencyProperty ScrollableProperty = DependencyProperty.Register(
        nameof(Scrollable), typeof(bool), typeof(TerminalSplitGrid),
        new PropertyMetadata(true, (owner, _) => ((TerminalSplitGrid)owner).Rebuild()));

    public bool Scrollable
    {
        get => (bool)GetValue(ScrollableProperty);
        set => SetValue(ScrollableProperty, value);
    }

    public IEnumerable? Sessions
    {
        get => (IEnumerable?)GetValue(SessionsProperty);
        set => SetValue(SessionsProperty, value);
    }

    /// <summary>Zero follows the automatic compact layout; otherwise this is the user-selected column count.</summary>
    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public TerminalSplitGrid()
    {
        Loaded += (_, _) =>
        {
            Unsubscribe();
            _collection = Sessions as INotifyCollectionChanged;
            if (_collection is not null) _collection.CollectionChanged += SessionsChanged;
            Rebuild();
        };
        Unloaded += (_, _) => Unsubscribe();
        // A live reorder: the grid (which outlives every layout rebuild) captures the mouse, so as the
        // cursor passes over another pane the backing collection reorders on the spot and the panes
        // reflow underneath — no drop target, no ghost, no held gesture. Double-clicking a header
        // maximizes that pane to fill the grid; double-clicking again restores the split.
        PreviewMouseLeftButtonDown += OnPaneMouseDown;
        PreviewMouseMove += OnPaneMouseMove;
        PreviewMouseLeftButtonUp += (_, _) => EndDrag();
        LostMouseCapture += (_, _) => EndDrag();
    }

    private static void OnSessionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (TerminalSplitGrid)d;
        grid.Unsubscribe();
        grid._collection = e.NewValue as INotifyCollectionChanged;
        if (grid._collection is not null) grid._collection.CollectionChanged += grid.SessionsChanged;
        grid.Rebuild();
    }

    private void SessionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private void Unsubscribe()
    {
        if (_collection is not null) _collection.CollectionChanged -= SessionsChanged;
        _collection = null;
    }

    private void Rebuild()
    {
        if (!IsLoaded) return;
        var sessions = Sessions?.Cast<TerminalSessionViewModel>().ToArray() ?? [];
        Children.Clear();
        _paneRows.Clear();
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        if (sessions.Length == 0) { MinHeight = 0; MinWidth = 0; Height = double.NaN; return; }

        var template = TryFindResource("TerminalPane") as DataTemplate;
        if (_maximized is not null && Array.IndexOf(sessions, _maximized) >= 0) { RenderMaximized(template); return; }
        _maximized = null;

        var columns = Columns > 0 ? Math.Min(Columns, sessions.Length) : (int)Math.Ceiling(Math.Sqrt(sessions.Length));
        var rows = (int)Math.Ceiling(sessions.Length / (double)columns);
        MinHeight = MinWidth = 0;
        Height = Width = double.NaN;
        var initialWidth = double.IsFinite(ViewportWidth) && ViewportWidth > 0
            ? Math.Max(180, (ViewportWidth - columns * 6) / columns) : 360;

        for (var row = 0; row < rows; row++)
        {
            RowDefinitions.Add(new RowDefinition { Height = Scrollable
                ? new GridLength(_rowSizes.GetValueOrDefault(row, 220)) : new GridLength(1, GridUnitType.Star), MinHeight = Scrollable ? 90 : 0 });
            if (row < rows - 1 || Scrollable) RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        }

        for (var row = 0; row < rows; row++)
        {
            var rowGrid = new Grid { HorizontalAlignment = HorizontalScrollable ? HorizontalAlignment.Left : HorizontalAlignment.Stretch };
            _paneRows.Add(rowGrid);
            SetRow(rowGrid, row * 2);
            Children.Add(rowGrid);
            var count = Math.Min(columns, sessions.Length - row * columns);
            var defaultWidth = ViewportWidth > 0 && double.IsFinite(ViewportWidth) ? Math.Max(180, (ViewportWidth - count * 6) / count) : initialWidth;
            for (var column = 0; column < count; column++)
            {
                var key = row * 1000 + column;
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = HorizontalScrollable ? new GridLength(_columnSizes.GetValueOrDefault(key, defaultWidth)) : new GridLength(1, GridUnitType.Star), MinWidth = HorizontalScrollable ? 180 : 0 });
                var pane = new ContentControl { Content = sessions[row * columns + column], ContentTemplate = template, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
                SetColumn(pane, column * 2);
                rowGrid.Children.Add(pane);
                if (column == count - 1 && !HorizontalScrollable) continue;
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
                FrameworkElement handle = HorizontalScrollable
                    ? CreateIndependentHandle(column * 2, false, rowGrid, row * 1000)
                    : new GridSplitter { Style = TryFindResource("ColumnSplitter") as Style, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
                SetColumn(handle, column * 2 + 1);
                rowGrid.Children.Add(handle);
            }
            if (HorizontalScrollable) rowGrid.Width = rowGrid.ColumnDefinitions.Sum(c => c.Width.Value);
        }
        for (var row = 1; row < RowDefinitions.Count; row += 2)
        {
            if (Scrollable)
            {
                var handle = CreateIndependentHandle(row - 1, vertical: true);
                SetRow(handle, row);
                Children.Add(handle);
                continue;
            }
            var splitter = new GridSplitter { Style = TryFindResource("RowSplitter") as Style, ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
            splitter.Height = 6;
            splitter.ToolTip = "Drag up or down to resize terminal panes";
            SetRow(splitter, row);
            Children.Add(splitter);
        }
    }

    private void FillViewportWidth()
    {
        if (!HorizontalScrollable || !double.IsFinite(ViewportWidth)) return;
        foreach (var grid in _paneRows)
        {
        var missing = ViewportWidth - grid.ColumnDefinitions.Sum(column => column.Width.Value);
        if (missing <= 0) continue;
        var count = (grid.ColumnDefinitions.Count + 1) / 2;
        for (var index = 0; index < grid.ColumnDefinitions.Count; index += 2)
        {
            var size = grid.ColumnDefinitions[index].Width.Value + missing / count;
            grid.ColumnDefinitions[index].Width = new GridLength(size);
            _columnSizes[_paneRows.IndexOf(grid) * 1000 + index / 2] = size;
        }
        grid.Width = grid.ColumnDefinitions.Sum(c => c.Width.Value);
        }
    }

    // Scrollable axes use fixed pane sizes. A Thumb updates only the preceding row/column;
    // the grid's desired extent grows or shrinks, instead of borrowing space from a neighbor.
    private Thumb CreateIndependentHandle(int index, bool vertical, Grid? owner = null, int keyOffset = 0)
    {
        var handle = new Thumb
        {
            Cursor = vertical ? Cursors.SizeNS : Cursors.SizeWE,
            Focusable = true,
            ToolTip = vertical ? "Resize the row above (other rows keep their height)" : "Resize the column to the left (other columns keep their width)",
            Background = (Brush)FindResource("BorderStrongBrush"),
        };
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(vertical ? FrameworkElement.HeightProperty : FrameworkElement.WidthProperty, 1d);
        border.SetValue(vertical ? FrameworkElement.VerticalAlignmentProperty : FrameworkElement.HorizontalAlignmentProperty,
            vertical ? (object)VerticalAlignment.Center : HorizontalAlignment.Center);
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Background)) { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var hitArea = new FrameworkElementFactory(typeof(Grid));
        hitArea.SetValue(Panel.BackgroundProperty, Brushes.Transparent);
        hitArea.AppendChild(border);
        handle.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = hitArea };
        handle.MouseEnter += (_, _) => handle.Background = (Brush)FindResource("AccentBrush");
        handle.MouseLeave += (_, _) => handle.Background = (Brush)FindResource("BorderStrongBrush");
        void Resize(double delta)
        {
            if (vertical)
            {
                var row = RowDefinitions[index];
                var size = Math.Max(row.MinHeight, row.Height.Value + delta);
                row.Height = new GridLength(size);
                _rowSizes[index / 2] = size;
            }
            else
            {
                var column = owner!.ColumnDefinitions[index];
                var size = Math.Max(column.MinWidth, column.Width.Value + delta);
                column.Width = new GridLength(size);
                _columnSizes[keyOffset + index / 2] = size;
                owner.Width = owner.ColumnDefinitions.Sum(c => c.Width.Value);
            }
        }
        handle.DragDelta += (_, e) => { Resize(vertical ? e.VerticalChange : e.HorizontalChange); e.Handled = true; };
        handle.KeyDown += (_, e) =>
        {
            if (e.Key == (vertical ? Key.Up : Key.Left)) { Resize(-10); e.Handled = true; }
            if (e.Key == (vertical ? Key.Down : Key.Right)) { Resize(10); e.Handled = true; }
        };
        return handle;
    }

    private void OnPaneMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (HeaderUnder(e.OriginalSource as DependencyObject) is not { } header ||
            FindParent<ContentControl>(header)?.Content is not TerminalSessionViewModel session) return;
        if (e.ClickCount == 2) { ToggleMaximize(session); e.Handled = true; _dragCandidate = null; return; }
        _dragCandidate = session;
        _dragStart = e.GetPosition(this);
    }

    private void OnPaneMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { EndDrag(); return; }
        if (_dragCandidate is null || _maximized is not null) return;
        var point = e.GetPosition(this);
        if (!_dragging)
        {
            if (Math.Abs(point.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragging = true;
            Cursor = Cursors.SizeAll;
            CaptureMouse();
        }
        if (Sessions is not IList sessions || SessionAt(point) is not { } target || ReferenceEquals(target, _dragCandidate)) return;
        var from = sessions.IndexOf(_dragCandidate);
        var to = sessions.IndexOf(target);
        if (from < 0 || to < 0 || from == to) return;
        if (sessions is ObservableCollection<TerminalSessionViewModel> observable) observable.Move(from, to);
        else { sessions.RemoveAt(from); sessions.Insert(to, _dragCandidate); }
    }

    private void EndDrag()
    {
        if (_dragCandidate is null && !_dragging) return;
        _dragCandidate = null;
        _dragging = false;
        ClearValue(CursorProperty);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private void ToggleMaximize(TerminalSessionViewModel session)
    {
        EndDrag();
        _maximized = ReferenceEquals(_maximized, session) ? null : session;
        Rebuild();
    }

    private TerminalSessionViewModel? SessionAt(Point point)
        => InputHitTest(point) is DependencyObject hit ? FindParent<ContentControl>(hit)?.Content as TerminalSessionViewModel : null;

    // The one pane the user maximized, stretched to fill the whole viewport with the split hidden.
    private void RenderMaximized(DataTemplate? template)
    {
        MinHeight = MinWidth = 0;
        Width = double.IsFinite(ViewportWidth) && ViewportWidth > 0 ? ViewportWidth : double.NaN;
        Height = double.IsFinite(ViewportHeight) && ViewportHeight > 0 ? ViewportHeight : double.NaN;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var pane = new ContentControl { Content = _maximized, ContentTemplate = template, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        SetRow(pane, 0);
        SetColumn(pane, 0);
        Children.Add(pane);
    }

    private static FrameworkElement? HeaderUnder(DependencyObject? source)
    {
        for (var current = source; current is not null;
             current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is FrameworkElement { Tag: "PaneDrag" } header) return header;
        return null;
    }

    private static T? FindParent<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }
}
