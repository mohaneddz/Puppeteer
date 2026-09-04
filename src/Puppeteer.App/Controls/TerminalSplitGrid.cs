using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
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
        AllowDrop = true;
        Loaded += (_, _) => Rebuild();
        Unloaded += (_, _) => Unsubscribe();
        DragOver += OnDragOver;
        Drop += OnDrop;
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
        RowDefinitions.Clear();
        ColumnDefinitions.Clear();
        if (sessions.Length == 0) return;

        var columns = Columns > 0 ? Math.Min(Columns, sessions.Length) : (int)Math.Ceiling(Math.Sqrt(sessions.Length));
        var rows = (int)Math.Ceiling(sessions.Length / (double)columns);

        for (var column = 0; column < columns; column++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 180 });
            if (column < columns - 1) ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        }
        for (var row = 0; row < rows; row++)
        {
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 90 });
            if (row < rows - 1) RowDefinitions.Add(new RowDefinition { Height = new GridLength(6) });
        }

        var template = TryFindResource("TerminalPane") as DataTemplate;
        foreach (var (session, index) in sessions.Select((session, index) => (session, index)))
        {
            var pane = new ContentControl { Content = session, ContentTemplate = template, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
            SetColumn(pane, (index % columns) * 2);
            SetRow(pane, (index / columns) * 2);
            Children.Add(pane);
        }

        for (var column = 1; column < ColumnDefinitions.Count; column += 2)
        {
            var splitter = new GridSplitter { Style = TryFindResource("ColumnSplitter") as Style, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
            SetColumn(splitter, column);
            SetRowSpan(splitter, RowDefinitions.Count);
            Children.Add(splitter);
        }
        for (var row = 1; row < RowDefinitions.Count; row += 2)
        {
            var splitter = new GridSplitter { Style = TryFindResource("RowSplitter") as Style, ResizeDirection = GridResizeDirection.Rows, ResizeBehavior = GridResizeBehavior.PreviousAndNext };
            SetRow(splitter, row);
            SetColumnSpan(splitter, ColumnDefinitions.Count);
            Children.Add(splitter);
        }
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(TerminalSessionViewModel)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TerminalSessionViewModel)) is not TerminalSessionViewModel moving || Sessions is not IList sessions) return;
        var from = sessions.IndexOf(moving);
        if (from < 0) return;
        var to = FindParent<ContentControl>(e.OriginalSource as DependencyObject)?.Content is TerminalSessionViewModel target
            ? sessions.IndexOf(target) : sessions.Count - 1;
        if (to < 0 || from == to) return;
        sessions.RemoveAt(from);
        if (from < to) to--;
        sessions.Insert(to, moving);
        e.Handled = true;
    }

    private static T? FindParent<T>(DependencyObject? source) where T : DependencyObject
    {
        for (var current = source; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
            if (current is T match) return match;
        return null;
    }
}
