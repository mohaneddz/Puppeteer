using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Puppeteer.App.Controls;

/// <summary>
/// A wrap panel that only realizes the item containers currently in view. WPF ships a virtualizing
/// stack panel but no virtualizing wrap panel, so a grid of hundreds of project cards would realize
/// every card at once and lag badly. Cells are uniform, which keeps the layout math — and the
/// scrolling — cheap: <see cref="ItemWidth"/> is the <i>minimum</i> cell width, and whatever is left
/// over after the column count is fixed gets shared out across the columns so the grid fills the
/// content area instead of leaving a ragged strip of dead space on the right.
/// </summary>
public sealed class VirtualizingWrapPanel : VirtualizingPanel, IScrollInfo
{
    public static readonly DependencyProperty ItemWidthProperty = DependencyProperty.Register(
        nameof(ItemWidth), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(316d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(VirtualizingWrapPanel),
        new FrameworkPropertyMetadata(182d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double ItemWidth { get => (double)GetValue(ItemWidthProperty); set => SetValue(ItemWidthProperty, value); }
    public double ItemHeight { get => (double)GetValue(ItemHeightProperty); set => SetValue(ItemHeightProperty, value); }

    private Size _cell = new(316, 182);
    private int _columns = 1;
    private Size _extent;
    private Size _viewport;
    private Point _offset;

    public ScrollViewer? ScrollOwner { get; set; }
    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }

    /// <summary>Fixes the column count and cell size for a given viewport, and the extent that follows
    /// from them. Every layout path goes through here so Measure and Arrange can never disagree about
    /// how many columns there are.</summary>
    private void ResolveCells(Size viewport, int itemCount)
    {
        var preferred = Math.Max(1, ItemWidth);
        // Let a column run a little under the preferred width rather than dropping it: with a hard
        // floor, a few pixels of window width cost a whole column and stretch the survivors a third
        // wider than intended, so the grid's column count lurches as the window resizes.
        var tolerance = preferred * 0.12;
        _columns = viewport.Width > 0 ? Math.Max(1, (int)((viewport.Width + tolerance) / preferred)) : 1;
        _cell = new Size(viewport.Width > 0 ? viewport.Width / _columns : preferred, ItemHeight);
        var rows = (int)Math.Ceiling((double)itemCount / _columns);
        _extent = new Size(viewport.Width, rows * _cell.Height);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var owner = ItemsControl.GetItemsOwner(this);
        var itemCount = owner?.Items.Count ?? 0;
        _ = InternalChildren; // forces the generator to initialize
        var generator = ItemContainerGenerator;

        UpdateScrollInfo(availableSize, itemCount);
        GetVisibleRange(itemCount, out var firstIndex, out var lastIndex);

        if (itemCount > 0 && lastIndex >= firstIndex)
        {
            var startPos = generator.GeneratorPositionFromIndex(firstIndex);
            var childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;
            using var _ = generator.StartAt(startPos, GeneratorDirection.Forward, true);
            for (var i = firstIndex; i <= lastIndex; i++, childIndex++)
            {
                var child = (UIElement)generator.GenerateNext(out var newlyRealized);
                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count) AddInternalChild(child);
                    else InsertInternalChild(childIndex, child);
                    generator.PrepareItemContainer(child);
                }
                child.Measure(_cell);
            }
        }

        CleanupChildren(firstIndex, lastIndex);
        return new Size(
            double.IsInfinity(availableSize.Width) ? _extent.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? _extent.Height : availableSize.Height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // MeasureOverride's availableSize is only an estimate for Star-sized Grid rows/columns — the
        // real allocation can come out different (e.g. collapsing the sidebar widens this column after
        // the panel already measured against the old, narrower width). If we only ever synced _viewport
        // during Measure, a mismatch here would stick: children stay realized for the wrong column
        // count/row range, leaving unused width on the right and a dead gap at the bottom that no later
        // Measure pass corrects (nothing tells WPF the size is wrong once _viewport "agrees" with itself).
        // Arrange gets the authoritative size, so reconcile here and force a follow-up Measure.
        if (Math.Abs(finalSize.Width - _viewport.Width) > 0.5 || Math.Abs(finalSize.Height - _viewport.Height) > 0.5)
        {
            _viewport = finalSize;
            ResolveCells(_viewport, ItemsControl.GetItemsOwner(this)?.Items.Count ?? 0);
            ScrollOwner?.InvalidateScrollInfo();
            InvalidateMeasure();
        }

        var owner = ItemsControl.GetItemsOwner(this);
        var columns = _columns;
        foreach (UIElement child in InternalChildren)
        {
            var itemIndex = owner?.ItemContainerGenerator.IndexFromContainer(child) ?? -1;
            if (itemIndex < 0) continue;
            var row = itemIndex / columns;
            var column = itemIndex % columns;
            child.Arrange(new Rect(
                column * _cell.Width - _offset.X,
                row * _cell.Height - _offset.Y,
                _cell.Width, _cell.Height));
        }
        return finalSize;
    }

    private void GetVisibleRange(int itemCount, out int firstIndex, out int lastIndex)
    {
        if (itemCount == 0) { firstIndex = 0; lastIndex = -1; return; }
        var columns = _columns;
        var firstRow = Math.Max(0, (int)(_offset.Y / _cell.Height));
        var rowsInView = (int)Math.Ceiling(_viewport.Height / _cell.Height) + 1;
        firstIndex = firstRow * columns;
        lastIndex = Math.Min(itemCount - 1, (firstRow + rowsInView + 1) * columns - 1);
    }

    private void CleanupChildren(int firstIndex, int lastIndex)
    {
        var generator = ItemContainerGenerator;
        for (var i = InternalChildren.Count - 1; i >= 0; i--)
        {
            var pos = new GeneratorPosition(i, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(pos);
            if (itemIndex < firstIndex || itemIndex > lastIndex)
            {
                generator.Remove(pos, 1);
                RemoveInternalChildRange(i, 1);
            }
        }
    }

    private void UpdateScrollInfo(Size availableSize, int itemCount)
    {
        // ScrollContentPresenter measures an IScrollInfo panel with Infinity on the scrolled axis for
        // some layout passes (e.g. right after a sibling row resizes the ListBox's available height).
        // Falling back to 0 there stranded the panel at "viewport 0" for that pass, which realized only
        // a row or two while the extent (driven by item count, not viewport) stayed full height —
        // leaving a dead gap below the last rendered row until something forced a remeasure with a real
        // size. Keep the last known-good viewport instead of collapsing it.
        var viewport = new Size(
            double.IsInfinity(availableSize.Width) ? _viewport.Width : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? _viewport.Height : availableSize.Height);
        var previousExtent = _extent;
        ResolveCells(viewport, itemCount);

        if (_extent != previousExtent) ScrollOwner?.InvalidateScrollInfo();
        if (viewport != _viewport) { _viewport = viewport; ScrollOwner?.InvalidateScrollInfo(); }

        var maxY = Math.Max(0, _extent.Height - _viewport.Height);
        if (_offset.Y > maxY) SetVerticalOffset(maxY);
    }

    public Size Extent => _extent;
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;

    public void SetVerticalOffset(double offset)
    {
        offset = Math.Max(0, Math.Min(offset, Math.Max(0, _extent.Height - _viewport.Height)));
        if (Math.Abs(offset - _offset.Y) < 0.001) return;
        _offset.Y = offset;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public void SetHorizontalOffset(double offset) { }

    private const double LineStep = 20;
    private const double WheelStep = 60;
    public void LineUp() => SetVerticalOffset(_offset.Y - LineStep);
    public void LineDown() => SetVerticalOffset(_offset.Y + LineStep);
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - WheelStep);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + WheelStep);

    // ScrollViewer normally reduces every wheel notch to one fixed-size MouseWheelUp/Down call, which
    // feels like a snap on a precision trackpad that reports many small deltas per gesture. Following
    // the raw delta instead gives continuous, proportional motion like scrolling a normal web page.
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (!e.Handled)
        {
            SetVerticalOffset(_offset.Y - e.Delta);
            e.Handled = true;
        }
        base.OnMouseWheel(e);
    }
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        var child = InternalChildren.Cast<UIElement>().FirstOrDefault(c => c.IsAncestorOf(visual));
        if (child is null) return rectangle;
        var owner = ItemsControl.GetItemsOwner(this);
        var itemIndex = owner?.ItemContainerGenerator.IndexFromContainer(child) ?? -1;
        if (itemIndex < 0) return rectangle;
        var row = itemIndex / _columns;
        var top = row * _cell.Height;
        var bottom = top + _cell.Height;
        if (top < _offset.Y) SetVerticalOffset(top);
        else if (bottom > _offset.Y + _viewport.Height) SetVerticalOffset(bottom - _viewport.Height);
        return rectangle;
    }
}
